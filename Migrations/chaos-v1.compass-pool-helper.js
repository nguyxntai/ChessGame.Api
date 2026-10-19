// Paste this definition into Compass. Defining it does not access or modify MongoDB.
// Matches ModeNormalization.SettingsFingerprint and GameSettings JSON property order.
globalThis.chaosPoolKey = function (settings) {
    if (!settings) throw new Error("Ticket has no Settings. Stop and inspect.");
    var wire = {
        mode: settings.Mode,
        region: settings.Region,
        initialSeconds: Number(settings.InitialSeconds),
        incrementSeconds: Number(settings.IncrementSeconds),
        protocolVersion: Number(settings.ProtocolVersion)
    };
    if (["Classic", "Chaos"].indexOf(wire.mode) < 0 ||
        typeof wire.region !== "string" || !/^[A-Z0-9-]{2,16}$/.test(wire.region) ||
        !Number.isInteger(wire.initialSeconds) || wire.initialSeconds < 60 || wire.initialSeconds > 7200 ||
        !Number.isInteger(wire.incrementSeconds) || wire.incrementSeconds < 0 || wire.incrementSeconds > 60 ||
        !Number.isInteger(wire.protocolVersion) || wire.protocolVersion < 1)
        throw new Error("Noncanonical ticket Settings. Stop and inspect before updating its pool key.");
    return require("crypto").createHash("sha256").update(JSON.stringify(wire), "utf8").digest("hex").toUpperCase();
};
