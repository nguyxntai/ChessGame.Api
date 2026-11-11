# Local multiplayer verification

This console probe uses only `127.0.0.1:28291`, replica set `cbw-multiplayer-test`, and a fresh
`cbw_multiplayer_test_<guid>` database per run. It does not read appsettings or production accounts.
Start a dedicated MongoDB replica-set process on that port before running:

```powershell
dotnet run --project Tools/MultiplayerVerification/MultiplayerVerification.csproj
```

The host needs .NET 8. The probe initializes the dedicated replica set if needed, acquires its
own runtime lease, and tests room creation/join/retry/start/release, simultaneous joins,
stale reservations, full rooms, Classic/Chaos normal/ranked settlement, released player seats,
and the production durable outbox loop. SignalR delivery uses a recorder. Databases are retained
for inspection. Stop only the MongoDB process started for this probe after verification.

The settlement cases seed durable participation records to isolate rating eligibility from
the independent no-participation rule. They do not simulate a complete played match or WAN traffic.
