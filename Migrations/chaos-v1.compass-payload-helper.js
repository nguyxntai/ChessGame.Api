// Paste this definition into Compass's embedded shell. Defining it does not access MongoDB.
// Used by the supervised offline migration after full backup and duplicate-key preflight.
// No eval(), filesystem loading, process.env or top-level await.
globalThis.normalizeChaosPayload = function normalizePayloadNode(value) {
    if (Array.isArray(value)) {
        value.forEach(normalizePayloadNode);
        return value;
    }
    if (!value || typeof value !== "object") return value;
    if (Object.prototype.hasOwnProperty.call(value, "aram")) {
        if (Object.prototype.hasOwnProperty.call(value, "chaos"))
            throw new Error("Payload contains both aram and chaos. Stop and inspect before continuing.");
        value.chaos = value.aram;
        delete value.aram;
    }
    ["mode", "ratingMode"].forEach(function (key) {
        if (typeof value[key] === "string" && value[key].trim().toUpperCase() === "ARAM")
            value[key] = "Chaos";
    });
    Object.keys(value).forEach(function (key) { normalizePayloadNode(value[key]); });
    return value;
};
