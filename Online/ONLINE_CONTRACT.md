# Online .NET — hợp đồng và kiểm tra thủ công

Ngày triển khai: 03/10/2026. Protocol REST/SignalR: `1`; luật ARAM: `2`.

## Phạm vi và nguồn luật

Đã triển khai phần backend trong `ChessGame.Api`: luật, snapshot, đồng hồ, SignalR,
reconnect/restart, matchmaking, phòng riêng, lịch sử, Elo và thưởng. Auth/profile/inventory/gacha
tiếp tục dùng các service và collection hiện có. Không seed dữ liệu tài khoản/trang bị.

## Cấu trúc thư mục Online

| Thư mục | Nội dung |
| --- | --- |
| `DTOs/` | Request, response, command, snapshot và event; mỗi DTO một file |
| `Controllers/` | MatchmakingController, MatchesController và RoomsController; mỗi controller một file |
| `Models/` | Document MongoDB và model trạng thái trận/ARAM; mỗi model một file |
| `Services/` | OnlineService cùng các phần partial Commands, Lobby, Presence và LiveConnections |
| `Hubs/` | GameHub cho SignalR |
| `Rules/` | GameRules và AramEngine |
| `Domain/` | Engine cờ thuần C# |
| `Infrastructure/` | OnlineStore và OnlineRuntimeLease: MongoDB, transaction, index, lease |
| `Workers/` | OnlineWorker xử lý deadline, recovery và outbox |
| `Settings/` | OnlineOptions |
| `Filters/` | OnlineExceptionFilter chuyển domain exception thành phản hồi HTTP |
| `Common/` | OnlineException, OnlineIdentity và OnlineJson |

Các type giữ namespace `ChessGame.Api.Online`; engine trong `Domain` giữ
`ChessButWeird.Domain`. Việc chia folder không đổi API, tên type, đăng ký DI hay dữ liệu MongoDB.
Các file `OnlineService.cs`, `OnlineCommands.cs`, `OnlineLobby.cs`, `OnlinePresence.cs`
trong `Services` vẫn là các phần của cùng một class `OnlineService`.

## Nguồn luật và phạm vi client

Engine Classic trong `Online/Domain` được lấy từ
`D:/Stuurdy/Chesss-but-Weird/Packages/com.chessbutweird.domain/Runtime`.
Các buff và kỹ năng ARAM được chuyển từ `AramBuffDefinition.cs`, `AramBuffRuntime.cs`,
`AramBuffRuntime.Extended.cs`, `AramBuffRuntime.Rifle.cs` và `ChessGame.Aram.cs` của Unity.
ARAM v2 dùng 26 buff của chế độ local hiện tại; giao thức online cũ của Unity chỉ dùng 6 buff.

**Unity chưa được sửa trong lần triển khai backend này.** `BackendWebSocketClient` hiện tại dùng
giao thức WebSocket của backend cũ, nên không thể gọi trực tiếp SignalR mới.
Unity cần một transport SignalR, DTO mới và presenter áp dụng snapshot thay cho tự xử lý online.
Swagger dùng để kiểm tra REST; các hub method cần client SignalR để gọi thủ công.

Không tạo/chạy test tự động, không chạy server, không gọi API hay ghi dữ liệu MongoDB để kiểm thử.
Chỉ build kiểm tra biên dịch. Kết quả build không xác nhận hoạt động transaction, hai client,
reconnect hay tính đúng của từng buff trong môi trường chạy thật.

## Chạy backend

- Dùng cấu hình MongoDB/JWT hiện có. MongoDB phải là **replica set hoặc sharded cluster**
  có transaction; không hỗ trợ standalone MongoDB cho online.
- Chạy một instance API xử lý online. Lease `online_leases/runtime` chỉ cho một instance sở hữu online;
  instance mới vẫn mở HTTP và chờ lease ở background, không làm startup crash.
  Cần backplane SignalR và thiết kế phân phối presence/ownership trước khi mở rộng nhiều instance.
- Startup tạo các index `online_*` và index ledger thưởng; không thay đổi schema các collection cũ.
- Worker giữ lease 15 giây và renew mỗi 3 giây. Khi mất lease, instance dừng để bảo toàn quyền xử lý.
- Sau khi lấy lease, worker renew trong lúc recovery; chỉ mở API online khi recovery hoàn tất.
  Trong lúc chờ/recovery, API online trả `503 OnlineTemporarilyUnavailable`; SignalR cần kết nối lại.
- Các mutation dùng transaction snapshot + majority write, ghi guard chung trước khi đọc/đổi trạng thái.
  Đây là cách ưu tiên tính nhất quán cho một instance; chưa tối ưu throughput cho nhiều trận đồng thời.

### Deploy trên Render

- Trong Settings của web service, đặt **Health Check Path** là `/api/health`. Endpoint này kiểm tra
  HTTP đang chạy, không phụ thuộc quyền sở hữu online, để Render chuyển traffic và tắt container cũ.
  Không dùng endpoint gameplay làm health check vì instance mới phải chờ instance cũ nhả lease.
- Giữ một instance, chưa bật autoscaling. Render vẫn có thể chạy container cũ/mới đồng thời trong deploy.
  Xem [quy trình zero-downtime deploy](https://render.com/docs/deploys#zero-downtime-deploys):
  Render chuyển traffic trước, đợi 60 giây rồi gửi SIGTERM cho container cũ.
- Trong khoảng chuyển giao này, HTTP/auth vẫn phục vụ nhưng online có thể tạm trả 503 khoảng một phút,
  cộng thời gian shutdown, chờ lease hết hạn nếu không nhả được, và recovery. Đồng hồ trận vẫn chạy.
  Đây chưa phải chuyển giao gameplay không gián đoạn; client cần retry/reconnect và tải lại snapshot.
- Log `Another instance owns online gameplay. HTTP is available; waiting...` là trạng thái chờ bình thường.
  Log `Online runtime ready; lease acquired and match recovery completed.` cho biết online đã sẵn sàng.
- Nếu chờ mãi sau deploy, kiểm tra API local hoặc service khác có dùng chung database production hay không.
  Tắt instance đó hoặc dùng database riêng cho development; không xóa lease khi instance đó còn chạy.

Các khóa cấu hình tùy chọn dưới section `Online` (hoặc biến môi trường `Online__...`):

| Khóa | Mặc định |
| --- | ---: |
| ProtocolVersion | 1 |
| QueueSeconds | 180 |
| ReadySeconds | 180 |
| ReconnectSeconds | 60 |
| DrawOfferSeconds | 30 |
| RoomSeconds | 1800 |
| InitialRatingRange | 150 |
| RatingRangePerSecond | 10 |
| EloK | 32 |

## State machine và chính sách

Ticket: `Queued -> Matched / Cancelled / Expired`.
Match: `AwaitingReady -> InProgress -> Finished`; `Cancelled` chỉ áp dụng trước khi bắt đầu.
Không bật bước chấp nhận riêng: `AcceptMatch` trả snapshot; `DeclineMatch` hủy trận chưa bắt đầu.
Phòng: `Open -> Started / Closed`. Phòng có tối đa hai thành viên; chủ rời phòng thì chuyển chủ cho
người còn lại, hết thành viên thì đóng. Phòng đã start không bị đóng/rời bằng API phòng.

`online_player_seats` có `_id = userId`: một người chỉ có một chỗ trong queue, phòng hoặc trận.
Ở trong phòng chặn queue. Match creation/pairing/cancel/start cập nhật seat cùng transaction.
Nếu pairing thắng cuộc đua cancel, DELETE ticket trả `Matched` kèm `matchId`; client có thể
`DeclineMatch` nếu trận vẫn `AwaitingReady`.

Match start yêu cầu cả hai `SetReady`, cả hai có kết nối hub và ARAM hoàn tất setup nếu áp dụng.
Loadout được kiểm tra quyền sở hữu/type/item còn active tại lúc tạo và start; sau start lưu snapshot,
nên việc equip khác về sau không đổi trang bị trong trận đang chơi.

Đồng hồ đếm theo thời gian UTC của backend, tiếp tục chạy lúc mất kết nối hoặc restart.
Mỗi move hợp lệ được cộng increment; kỹ năng tiêu tốn lượt cũng được cộng increment,
kỹ năng miễn lượt không được cộng. Đồng hồ chưa chạy trong draft/setup/loading.
Hết ready/setup deadline thì hủy, không đổi Elo/thưởng. Mất toàn bộ kết nối hub của một người
thì bắt đầu grace; hết grace thua abandonment. Nếu hai deadline abandonment bằng nhau thì hòa.
Nếu timeout và abandonment cạnh tranh, deadline xảy ra trước quyết định kết quả.

Sau restart: tải snapshot đã lưu, xử lý deadline đã hết; nếu còn chơi, đánh dấu offline và cho
grace reconnect (không kéo dài deadline reconnect đã có). Lệnh, lịch sử, outbox và kết quả vẫn ở DB.

Classic: chiếu hết, stalemate, các vị trí thiếu quân cơ bản, 5 lần lặp/75 nước tự hòa;
3 lần lặp/50 nước dùng `ClaimDraw` cho trạng thái hiện tại. Không hỗ trợ claim theo nước dự định.
ARAM: không áp dụng draw claim/lặp/FEN; luật buff có thể tạo hoặc hủy quân và chiếu hết khác Classic.
FEN là `null` trong ARAM; client phải dùng board + toàn bộ `aram`.

## REST

Mọi endpoint yêu cầu `Authorization: Bearer <access token>`; danh tính lấy từ JWT.
Không nhận userId, MMR, màu, clock, kết quả hay phần thưởng do client khai báo.

| Endpoint | Mục đích |
| --- | --- |
| POST `/api/matchmaking/tickets` | Tạo queue; body gồm `requestId`, `settings` |
| GET `/api/matchmaking/tickets/current` | Ticket gần nhất, kể cả terminal; không có thì `null` |
| DELETE `/api/matchmaking/tickets/{ticketId}` | Cancel hoặc trả trạng thái đã matched |
| GET `/api/matches/current` | Trận chưa hoàn tất hoặc `null` |
| GET `/api/matches/{matchId}` | Metadata, người chơi, trạng thái, result |
| GET `/api/matches/{matchId}/state` | Snapshot đầy đủ |
| GET `/api/matches/{matchId}/moves?page=1&pageSize=20&afterSequence=0` | Lịch sử move/ability |
| GET `/api/users/me/matches?page=1&pageSize=20` | Trận Finished/Cancelled của tài khoản |
| GET `/api/matches/{matchId}/result` | Kết quả chính thức; chưa có thì `ResultNotReady` |
| POST `/api/rooms` | Tạo phòng; body gồm `requestId`, `settings` |
| POST `/api/rooms/join` | Body `{"code":"ABCDEF12"}` |
| GET `/api/rooms/{roomId}` | Phòng của thành viên |
| PATCH `/api/rooms/{roomId}/settings` | Chủ đổi settings trước start |
| POST `/api/rooms/{roomId}/leave` | Rời phòng trước start |
| POST `/api/rooms/{roomId}/start` | Chủ tạo trận khi đủ hai người |
| DELETE `/api/rooms/{roomId}` | Chủ đóng phòng trước start |

Ví dụ queue hoặc create room:

```json
{
  "requestId": "moi-request-mot-id-on-dinh",
  "settings": {
    "mode": "Classic",
    "region": "VN",
    "initialSeconds": 600,
    "incrementSeconds": 5,
    "protocolVersion": 1
  }
}
```

Mode nhận `Classic` hoặc `Aram`; initial 60–7200 giây, increment 0–60 giây.
MMR lấy từ `users.stats.elo`. Pair khi settings khớp và chênh lệch Elo nằm trong range của cả hai;
range mở rộng theo thời gian queue. Queue/reward không dựa vào rank client gửi.
`requestId` lặp với cùng payload trả lại ticket/phòng cũ, payload khác trả `RequestIdConflict`.
Muốn tạo lượt queue/phòng mới sau khi terminal, dùng requestId mới.
Pagination: page 1–100000, pageSize 1–100; thứ tự move tăng theo sequence.

## SignalR `/hubs/game`

Access token dùng bearer header; browser WebSocket/SSE có thể dùng `access_token` query chỉ tại hub.
Kết nối được đóng khi token hết hạn; client refresh JWT bằng auth hiện có rồi reconnect.
Tham khảo [tài liệu xác thực SignalR của Microsoft](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz).

| Method | Tham số |
| --- | --- |
| SubscribeMatch | matchId |
| ReplayEvents | matchId, afterSequence, limit (1–100, mặc định 100) |
| AcceptMatch / DeclineMatch / SetReady | matchId |
| SubmitMove | matchId, commandId, expectedVersion, move |
| UseAbility | matchId, commandId, expectedVersion, ability |
| Resign / OfferDraw / ClaimDraw | matchId, commandId |
| RespondDraw | matchId, offerId, accept |
| RequestRematch | matchId |
| RespondRematch | matchId, accept |

Move body: `{"from":"e2","to":"e4","promotion":null}`.
Promotion bắt buộc khi tốt tới hàng cuối, dùng `Queen`, `Rook`, `Bishop`, `Knight`.
Tọa độ luôn canonical: a1 ở phía White, không gửi tọa độ đã xoay camera của Black.

`SubmitMove`/`UseAbility`/resign/draw trả:

```json
{
  "commandId": "id-cua-lenh",
  "accepted": true,
  "errorCode": null,
  "stateVersion": 8,
  "eventSequence": 10,
  "serverTime": "UTC timestamp",
  "replayed": false
}
```

`commandId` dài 1–80 ký tự, scope `(matchId,userId,commandId)`.
Retry giữ nguyên toàn bộ payload và expectedVersion; trả ack đã lưu với `replayed=true`, không áp dụng lần hai.
Reused ID với payload/version khác trả `CommandIdConflict`. Nếu bị stale, lấy snapshot và tạo commandId mới.
Rejection hợp lệ được lưu ack và gửi `CommandRejected` riêng cho người gửi; malformed/auth errors là HubException.
REST domain errors có `{code,message,stateVersion}`; lỗi JWT/model-binding của framework vẫn dùng phản hồi ASP.NET.

Events: `QueueStatusChanged`, `MatchFound`, `MatchCancelled`, `PlayerReadyChanged`, `MatchStarted`,
`GameStateUpdated`, `CommandRejected`, `DrawOffered`, `DrawResolved`, `PlayerConnectionChanged`,
`MatchEnded`, `RematchRequested`, `RematchResolved`, `RoomUpdated`, `RoomClosed`.
Envelope: `{eventId,type,matchId,sequence,stateVersion,serverTime,payload}`.
Event trận thường có `payload.state`; move/ability còn có `payload.commandId` và `payload.command`.
Event hết hạn draw có offerId/reason riêng. Event ticket/phòng không có match sequence.
`RoomClosed` có `payload.room` và `reason`. Rematch thành công có `state.rematchId` ở trận cũ và MatchFound trận mới.
Rematch cần đồng ý hai phía trong 2 phút; dùng loadout mới, không tính Elo cho trận rematch.

### Snapshot và reconnect không mất event

Snapshot có board (ID quân ổn định, kind/team/square/hasMoved/forward), turn, quyền nhập thành/en passant,
clocks/serverTime, stateVersion, eventSequence, ready/presence/loadout, draw offer, result và ARAM.
Draft options của đối phương bị ẩn; buff đã chọn và hiệu ứng gameplay công khai.

1. Đăng ký callback tất cả event trước khi mở kết nối; buffer event theo matchId/sequence.
2. Reconnect bằng JWT hợp lệ, gọi `SubscribeMatch(matchId)`.
3. Thay state bằng `response.state`, đặt watermark `response.resumeAfterSequence`.
4. Bỏ event có sequence <= watermark; áp dụng event lớn hơn theo thứ tự sequence.
5. Nếu thiếu sequence, gọi `ReplayEvents` từ watermark và phân trang đến latestSequence.
6. Có thể gọi SubscribeMatch lần nữa để thay bằng full state nếu không muốn replay toàn bộ.

Push được gửi tới user JWT đã xác thực, nên không có khoảng trống phải đợi join group sau snapshot.
Mỗi method/read vẫn kiểm tra membership trong DB. Outbox commit cùng state; event delivery có thể lặp
khi process ngắt giữa gửi và đánh dấu Published. Client deduplicate bằng eventId/sequence.
Moves REST là lịch sử lệnh, không thay thế ReplayEvents cho readiness/presence/draw/result.

## ARAM: ID buff, command và snapshot

Server chọn ngẫu nhiên một tier chung cho hai người; mỗi người được ba lựa chọn khác nhau trong tier đó,
chọn một buff. Mọi RNG/effect được tính trên server và lưu cùng command.
Trong AwaitingReady vẫn gọi UseAbility với expectedVersion để draft/setup; gameplay chỉ bắt đầu khi xong setup.

| ID | Buff | Cơ chế / command |
| --- | --- | --- |
| 0 | CommandantPawn | `SelectTargets` với 3 pawn ID; đi thẳng hai ô trống |
| 1 | StrongFortress | Tự động; bỏ tối đa một loại hạn chế nhập thành, đích vẫn an toàn |
| 2 | FreestyleLeap | Tự động; knight tới các ô trên tuyến L, không gồm chéo 2x2 |
| 3 | Doppelganger | `SelectTargets` gồm một Knight và Bishop; `ToggleSwap` mỗi 5 lượt chủ |
| 4 | SuicideBomber | Queen gốc nổ một lần khi bị bắt, hủy quân không phải King trong 3x3, gồm capturer |
| 5 | FlyingThunderGod | Queen gốc tới ô trống bằng SubmitMove; tối đa 5 teleport, cooldown 5 lượt |
| 6 | NobleSacrifice | Pawn bắt pawn đồng minh rồi có Bloodthirsty |
| 7 | AbsoluteSniper | Bishop bắt non-pawn từ >=4 ô lấy charge; `Snipe`, hết sau 5 lượt chủ |
| 8 | GamblingLeadsToMisery | Pawn bắt địch: 30% thêm nước, cùng pawn, tối đa 2 lần |
| 9 | LootBox | Crate ở round 25/50; nhận crate thì `Deploy` Rook/Queen trong nửa bàn của chủ |
| 10 | PeaceTShirt | Sau 15 lượt không bị chiếu: `Recruit`, lấy non-King/non-Queen địch vào ba hàng đầu |
| 11 | RiseOfPawn | Pawn lùi một ô trống; về hàng đáy thành mine, địch bước vào bị hủy |
| 12 | MobileFortress | `LoadPawn` vào Rook; Rook chết thì `Deploy` pawn trong 3x3, hàng cuối thành Queen |
| 13 | HidingKing | `HideKing` đổi chỗ với đồng minh hàng đáy; lúc đầu và mỗi 10 lượt chủ |
| 14 | GachaBanner | Ticket chiến đấu từ capture; `BattleGacha` tối đa 2/lượt: 70/10/10/9/1% P/N/B/R/Q |
| 15 | SubstituteNinjutsu | Lần đầu bị chiếu: teleport King tới ô an toàn trong nửa bàn, để decoy |
| 16 | HighTechEra | Rook gốc bắt qua đúng một blocker; mất cannon sau 10 lượt không bắn |
| 17 | QueensBetrayal | Queen thêm ở hàng thứ ba; mỗi lượt chủ 10% đổi phía |
| 18 | DefinitionOfAram | Round 10/15 sụp cặp file ngoài; hủy quân trên ô sụp |
| 19 | RngFiesta | Đổi quân ban đầu trừ King/Queen thành P/N/B/R ngẫu nhiên |
| 20 | IFrameRoll | Khi checkmate: `Escape` tới ô an toàn trong 5x5 quanh King, tối đa 3 lần |
| 21 | GhostArmy | Đi xuyên quân mình, không dùng xuyên để chiếu King địch |
| 22 | PlagueTown | Capturer bị nhiễm và chết sau 4 lượt chủ tiếp theo, cả King/Queen |
| 23 | CustomizeArmy | `ConfirmFormation` gửi toàn bộ vị trí quân trong nửa bàn; 120 giây, unchanged hoàn buff Gold |
| 24 | PawnsRevolution | Giữ King; lấp nửa bàn còn lại bằng Pawn |
| 25 | OneManArmy | Chỉ giữ King, đi thêm hai ô thẳng; `AimRifle`/`FireRifle`/`ExitRifle` |

Ví dụ ability body:

```json
{"kind":"SelectBuff","buffId":3}
{"kind":"SelectTargets","pieceIds":[12,14]}
{"kind":"HideKing","targetPieceId":9}
{"kind":"LoadPawn","pieceId":9,"targetPieceId":17}
{"kind":"Snipe","pieceId":11,"targetPieceId":25}
{"kind":"Recruit","targetPieceId":25,"target":"a3"}
{"kind":"Deploy","target":"c2"}
{"kind":"Escape","target":"e3"}
{"kind":"ConfirmFormation","formation":[{"pieceId":1,"square":"a8"}]}
{"kind":"AimRifle"}
{"kind":"FireRifle","yaw":180,"pitch":10}
```

Formation phải gửi đủ quân của người dùng, ID/vị trí không trùng, không làm King mình bị chiếu;
ví dụ một quân phía trên chỉ minh họa cấu trúc, không phải formation hợp lệ.
Formation chưa gửi khi hết 120 giây dùng bố trí gốc, hoàn buff Gold theo luật hiện tại.
Nếu Gold mới là Doppelganger thì vẫn cần SelectTargets trước setup deadline.
Pending reinforcement chỉ cho chủ của deployment đầu tiên gọi Deploy; nước thường và kỹ năng khác đợi.
Snapshot có `aram.phase`, `sides`, `deployments`, `forcedPawn`, `escapeTeam`, `mines`, `crates`,
`collapsedFiles`, effects theo pieceId, lượt chủ/cooldown/charges, và rifle session/deadline.
Battle tickets không liên quan wallet tickets; inventory không thay đổi vì buff chiến đấu.

### Rifle 3D trên server

Theo lựa chọn của bạn, client chỉ gửi góc yaw/pitch. Server lấy origin từ vị trí King,
raycast tới hitbox gần nhất trên bàn và tự xác định victim; không nhận `hitPieceId` do client báo.
Phiên ngắm tối đa 15 giây; fire/miss/đụng King/Queen/quân mình tiêu charge,
recharge sau 3 lượt chủ; ExitRifle giữ charge. Rifle không tiêu lượt. Nước thường bị chặn khi đang ngắm.

Online dùng hệ tọa độ riêng: a1=(0,0,0), file=+X, rank=+Z, up=+Y, tile=1.
Origin=(King.file,0.95,King.rank); yaw=0 nhìn +Z, yaw=90 nhìn +X; pitch dương nhìn xuống;
pitch chỉ -80..80 độ, range 500 đơn vị, board plane y=0 chặn tia hướng xuống.

Hitbox AABB canonical được snapshot trong `aram.hitboxes`: halfWidth/halfDepth=0.3;
height King/Queen=1.2, Pawn=0.65, Rook/Bishop/Knight=0.9.
Đây là **hợp đồng hitbox online mới**, không phải số đo đã xác nhận từ mesh/FBX đang dùng.
Unity cần đồng bộ collider online và chuyển camera/ray từ world space sang hệ tọa độ này;
không dùng bounds của cosmetic mesh để quyết định rifle hit. Collider/occluder của arena mỹ thuật
không tham gia simulation; bàn và hitbox quân là hình học gameplay có thẩm quyền.

## Kết quả, Elo và thưởng

Finish lưu result, tăng stats, đổi Elo, cộng wallet và ghi currency ledger trong cùng transaction.
Status Finished + index ledger `(userId,requestId,entryIndex)` chống áp dụng lần hai.
Không có endpoint nhận victory/result từ client.
Matchmaking tính Elo theo K=32 với rating server; phòng riêng/rematch không đổi Elo.
Thưởng dùng `MatchRewardPolicy.CalculateNetworkReward` hiện có của Unity:

| Kết quả | Golds | Diamonds | Tickets |
| --- | ---: | ---: | ---: |
| Thắng | 360 | 12 | 1 |
| Hòa | 180 | 6 | 0 |
| Thua | 85 | 3 | 0 |

## Danh sách kiểm tra thủ công

1. Chạy API với replica set; login hai tài khoản có inventory hợp lệ, kết nối hub bằng hai JWT.
2. Queue cùng settings: kiểm tra hai ticket cùng matchId, màu khác nhau, current match giống nhau.
3. SetReady hai phía: kiểm tra MatchStarted và clocks; gửi nước đúng/sai lượt, nước trái luật, version cũ.
4. Retry đúng commandId/payload: board/version không đổi lần hai; payload khác bị CommandIdConflict.
5. Race cancel/pair và room start/queue: không có hai active seat hoặc match mồ côi.
6. Tài khoản thứ ba gọi state/moves/result/hub: Forbidden; không nhận event trận riêng.
7. Mở hai kết nối cùng tài khoản rồi đóng một: vẫn connected. Đóng cả hai: grace chạy; reconnect trước/sau deadline.
8. Ngắt kết nối trong lúc có move/result, resubscribe/buffer/replay: kiểm tra sequence không mất và lặp được bỏ.
9. Ready/setup timeout, clock timeout, resign, draw offer hết hạn, accept/decline draw, rematch.
10. Restart khi đang queue/ready/playing và sau finish: snapshot/seat/clock phục hồi đúng policy, thưởng không tăng lại.
11. Kiểm tra từng buff theo bảng, đặc biệt explosion/cannon/phasing/check safety, pawn extra turn,
    deployment/promotion, collapse, plague, formation timeout và escape.
12. Rifle: dùng collider canonical; thử góc hit/miss, occlusion quân trước/board, target King/Queen,
    shot expiry và cooldown, reconnect giữa phiên ngắm.
13. Đọc `/result`, `/users/me/matches`, `/users/me` và ledger: stats/Elo/wallet đúng một lần;
    kiểm tra equip sau start không thay loadout snapshot đang chơi.
