// OFFLINE ONLY. Default is read-only plan. See Documentation/ranks-and-chaos.md.
// Nothing in this script resets rating, games, RD, volatility, peak, lastRatedAt or policy IDs.
const crypto = require("crypto");
const APPLY = process.env.CHAOS_MIGRATION_APPLY === "YES";
const OFFLINE = process.env.CHAOS_MIGRATION_OFFLINE === "YES";
const MIGRATION = "chaos-v1";
// API DatabaseName can differ from the default database in its connection URI.
// Require the operator to select the same database explicitly for both plan and apply.
const targetDatabase = process.env.CHAOS_MIGRATION_DATABASE?.trim();
if (!targetDatabase || ["admin", "config", "local"].includes(targetDatabase))
    throw new Error("Set CHAOS_MIGRATION_DATABASE to the API MongoDb:DatabaseName before plan/apply.");
const migrationDb = db.getSiblingDB(targetDatabase);
const own = (o, k) => Object.prototype.hasOwnProperty.call(o, k);
const copy = value => EJSON.parse(EJSON.stringify(value, { relaxed: false }), { relaxed: false });
function stable(value) {
    const sort = x => Array.isArray(x) ? x.map(sort) : x && typeof x === "object"
        ? Object.fromEntries(Object.keys(x).sort().map(k => [k, sort(x[k])])) : x;
    return JSON.stringify(sort(JSON.parse(EJSON.stringify(value, { relaxed: false }))));
}
const equal = (a, b) => stable(a) === stable(b);
const digest = value => crypto.createHash("sha256").update(stable(value)).digest("hex");
const legacyMode = value => typeof value === "string" && value.trim().toUpperCase() === "ARAM";

function rename(obj, oldKey, newKey, path) {
    if (!obj || !own(obj, oldKey)) return;
    if (own(obj, newKey) && !equal(obj[oldKey], obj[newKey])) throw new Error("Different dual keys at " + path);
    if (!own(obj, newKey)) obj[newKey] = obj[oldKey];
    delete obj[oldKey]; // Dual keys may be consolidated only after full value equality.
}
function normalizeSettings(settings) {
    if (settings && legacyMode(settings.Mode)) settings.Mode = "Chaos";
}
function payload(node, path) {
    if (Array.isArray(node)) { node.forEach((v, i) => payload(v, path + "[" + i + "]")); return; }
    if (!node || typeof node !== "object") return;
    rename(node, "aram", "chaos", path + ".aram");
    for (const key of ["mode", "ratingMode"]) if (legacyMode(node[key])) node[key] = "Chaos";
    for (const [key, value] of Object.entries(node)) payload(value, path + "." + key);
}
function poolKey(settings) {
    if (!settings || !["Classic", "Chaos"].includes(settings.Mode)) throw new Error("Unsupported persisted ticket settings");
    // Exact property order and ASCII values of OnlineJson.Write(GameSettings). No EJSON in this hash.
    const number = x => Number(x?.valueOf());
    const wire = { mode: settings.Mode, region: settings.Region, initialSeconds: number(settings.InitialSeconds),
        incrementSeconds: number(settings.IncrementSeconds), protocolVersion: number(settings.ProtocolVersion) };
    if (!/^[A-Z0-9-]{2,16}$/.test(wire.region) || !Number.isInteger(wire.initialSeconds) ||
        !Number.isInteger(wire.incrementSeconds) || !Number.isInteger(wire.protocolVersion))
        throw new Error("Noncanonical ticket settings; inspect before computing pool hash");
    return crypto.createHash("sha256").update(JSON.stringify(wire)).digest("hex").toUpperCase();
}
function transform(collection, doc) {
    if (collection === "users") {
        rename(doc.ratings, "aram", "chaos", "ratings.aram");
        rename(doc.onlineColors, "aram", "chaos", "onlineColors.aram");
    } else if (["online_matches", "online_rooms", "online_tickets"].includes(collection)) {
        normalizeSettings(doc.Settings);
        if (collection === "online_matches") {
            rename(doc, "Aram", "chaos", "Aram");
            if (doc.Result && legacyMode(doc.Result.RatingMode)) doc.Result.RatingMode = "Chaos";
            // Result.RatingPolicy and all pre-existing result values/snapshots remain exact.
        }
        if (collection === "online_tickets") doc.PoolKey = poolKey(doc.Settings);
        // CreationFingerprint is deliberately opaque and unchanged. The API accepts both hashes.
    } else if (["online_events", "online_moves"].includes(collection) && typeof doc.PayloadJson === "string") {
        const parsed = JSON.parse(doc.PayloadJson);
        const before = JSON.stringify(parsed);
        payload(parsed, "PayloadJson");
        if (JSON.stringify(parsed) !== before) doc.PayloadJson = JSON.stringify(parsed);
    }
    // online_commands contain no mode/settings: CommandId, Fingerprint and Ack remain byte-for-byte.
    return doc;
}

async function main() {
    const lease = await migrationDb.online_leases.findOne({ _id: "runtime", ExpiresAt: { $gt: new Date() } });
    if (APPLY && (!OFFLINE || lease)) throw new Error("Stop every API/worker and wait for runtime lease expiry; acknowledge OFFLINE=YES.");
    const changes = [], conflicts = [], totals = {}, queuedPools = new Set();
    const markers = migrationDb.getCollection("schema_migrations"), backups = migrationDb.getCollection("_chaos_v1_backup");
    let plannedBytes = 0, poolChanges = 0, conflictCount = 0;
    const reportConflict = conflict => {
        conflictCount++;
        if (conflicts.length < 50) conflicts.push(conflict);
    };
    async function planChange(collection, original, after) {
        const id = original?._id ?? after._id;
        const key = collection + ":" + EJSON.stringify(id);
        const prior = await backups.findOne({ _id: key });
        if (prior && (prior.afterHash !== digest(after) || !equal(prior.original, original)))
            reportConflict({ collection, id: EJSON.stringify(id), reason: "Backup journal conflicts; inspect before resuming" });
        // A read-only plan retains counts, not copies of every document in the database.
        if (!APPLY) return;
        plannedBytes += 2 * (stable(original).length + stable(after).length);
        if (plannedBytes > 32 * 1024 * 1024)
            throw new Error("Apply plan exceeds the 32 MiB document budget. No migration writes made; use an offline batched migration procedure.");
        changes.push({ collection, id, original, after });
    }
    printjson({ database: migrationDb.getName(), apply: APPLY, phase: "Preflight", batchSize: 25 });
    for (const collection of ["users", "online_matches", "online_rooms", "online_tickets", "online_events", "online_moves"]) {
        let scanned = 0, changed = 0;
        const cursor = migrationDb.getCollection(collection).find().batchSize(25);
        try {
            while (await cursor.hasNext()) {
                const original = await cursor.next();
                scanned++;
                let after;
                try { after = transform(collection, copy(original)); }
                catch (e) {
                    reportConflict({ collection, id: EJSON.stringify(original._id), reason: e.message });
                    continue;
                }
                if (collection === "online_tickets" && after.Status === "Queued") queuedPools.add(after.PoolKey);
                if (!equal(original, after)) { await planChange(collection, original, after); changed++; }
                if (scanned % 250 === 0) printjson({ collection, scanned, changed, phase: "Preflight" });
            }
        } finally { await cursor.close(); }
        totals[collection] = { scanned, changed };
        printjson({ collection, scanned, changed, phase: "CollectionComplete" });
    }
    // Pools are derived cursors, not player data. Rebuild their canonical keys, resetting fairness cursors.
    const desired = new Map([...queuedPools].map(key => [key, { _id: key, LastServedAt: new Date(0), CursorAt: new Date(0), CursorId: "" }]));
    const poolCursor = migrationDb.online_queue_pools.find().batchSize(25);
    try {
        while (await poolCursor.hasNext()) {
            const original = await poolCursor.next();
            const after = desired.get(original._id) ?? null;
            if (!equal(original, after)) { await planChange("online_queue_pools", original, after); poolChanges++; }
            desired.delete(original._id);
        }
    } finally { await poolCursor.close(); }
    for (const after of desired.values()) { await planChange("online_queue_pools", null, after); poolChanges++; }

    let indexes;
    try { indexes = await migrationDb.users.getIndexes(); }
    catch (e) { if (e.code === 26) indexes = []; else throw e; }
    const newIndexes = [
        { name: "idx_users_leaderboard_chaos", key: { isActive: 1, "ratings.chaos.rating": -1, _id: 1 } },
        { name: "idx_users_established_chaos", key: { isActive: 1, "ratings.chaos.rating": -1, _id: 1 },
            partialFilterExpression: { "ratings.chaos.ratedGames": { $gte: 10 } } }
    ];
    for (const spec of newIndexes) {
        const existing = indexes.find(i => i.name === spec.name);
        if (existing && (existing.unique || existing.sparse || JSON.stringify(existing.key) !== JSON.stringify(spec.key) ||
            !equal(existing.partialFilterExpression ?? null, spec.partialFilterExpression ?? null)))
            reportConflict({ collection: "users", id: spec.name, reason: "Canonical index has different definition" });
    }
    const legacyIndexes = indexes.filter(i => ["idx_users_leaderboard_aram", "idx_users_established_aram"].includes(i.name));
    for (const index of legacyIndexes) {
        const partial = index.name === "idx_users_established_aram" ? { "ratings.aram.ratedGames": { $gte: 10 } } : null;
        if (index.unique || index.sparse || JSON.stringify(index.key) !== JSON.stringify({ isActive: 1, "ratings.aram.rating": -1, _id: 1 }) ||
            !equal(index.partialFilterExpression ?? null, partial))
            reportConflict({ collection: "users", id: index.name, reason: "Legacy index has unexpected definition; preserve and inspect" });
    }
    printjson({ database: migrationDb.getName(), apply: APPLY, totals, poolChanges,
        createIndexes: newIndexes.map(i => i.name), dropIndexes: legacyIndexes.map(i => i.name), conflictCount,
        conflictsTruncated: conflictCount > conflicts.length, conflicts });
    if (conflictCount) throw new Error("Preflight failed; no migration writes made. Resolve conflicts from evidence, then rerun (first 50 shown).");
    if (!APPLY) { print("READ-ONLY PLAN. No writes. Back up the full database before offline apply."); return; }

    await markers.updateOne({ _id: MIGRATION }, { $set: { status: "Running", startedAt: new Date() } }, { upsert: true, writeConcern: { w: "majority" } });
    for (const change of changes) {
        const collection = migrationDb.getCollection(change.collection);
        const key = change.collection + ":" + EJSON.stringify(change.id);
        const afterHash = digest(change.after);
        const priorBackup = await backups.findOne({ _id: key });
        if (priorBackup && (priorBackup.afterHash !== afterHash || !equal(priorBackup.original, change.original)))
            throw new Error("Backup journal conflicts at " + key + "; keep server offline and inspect.");
        await backups.updateOne({ _id: key }, { $setOnInsert: { collection: change.collection, originalId: change.id,
            original: change.original, afterHash, createdAt: new Date() } }, { upsert: true, writeConcern: { w: "majority" } });
        // Check again after journaling. Maintenance mode is required; never overwrite concurrent changes.
        const current = await collection.findOne({ _id: change.id });
        if (!equal(current, change.original)) throw new Error("Document changed during migration at " + key);
        const options = { writeConcern: { w: "majority" } };
        if (change.after === null) await collection.deleteOne({ _id: change.id }, options);
        else if (change.original === null) await collection.insertOne(change.after, options);
        else {
            const result = await collection.replaceOne({ _id: change.id, $expr: { $eq: ["$$ROOT", { $literal: change.original }] } }, change.after, options);
            if (result.matchedCount !== 1) throw new Error("Concurrent write at " + key);
        }
    }
    for (const spec of newIndexes) {
        const { key, ...options } = spec;
        await migrationDb.users.createIndex(key, options);
    }
    for (const index of legacyIndexes) await migrationDb.users.dropIndex(index.name);
    await markers.updateOne({ _id: MIGRATION }, { $set: { status: "Completed", completedAt: new Date() } }, { writeConcern: { w: "majority" } });
    print("Completed. Keep API offline, rerun read-only plan, and verify against the backup before rollout.");
}
await main();
