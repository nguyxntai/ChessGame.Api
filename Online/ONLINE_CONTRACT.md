# Online .NET — hợp đồng và kiểm tra thủ công

Protocol REST/SignalR: `1`; trận Chaos mới dùng luật `3` (35 buff, đồng bộ practice).
Trận đã lưu luật `2` tiếp tục dùng engine/catalog 26 buff, không chuyển luật giữa trận.

Rank, JSON mẫu, migration và checklist mới: [Rank và chuyển đổi Chaos](../Documentation/ranks-and-chaos.md).
Phải hoàn tất migration offline trước khi chạy bản backend này trên dữ liệu cũ.

## Phạm vi và nguồn luật

Đã triển khai phần backend trong `ChessGame.Api`: luật, snapshot, đồng hồ, SignalR,
reconnect/restart, matchmaking, phòng riêng, lịch sử, Elo và thưởng. Auth/profile/inventory/gacha
tiếp tục dùng các service và collection hiện có. Không seed dữ liệu tài khoản/trang bị.

## Cấu trúc thư mục Online

| Thư mục | Nội dung |
| --- | --- |
| `DTOs/` | Request, response, command, snapshot và event; mỗi DTO một file |
| `Controllers/` | MatchmakingController, MatchesController và RoomsController; mỗi controller một file |
| `Models/` | Document MongoDB và model trạng thái trận/CHAOS; mỗi model một file |
| `Services/` | OnlineService cùng các phần partial Commands, Lobby, Presence và LiveConnections |
| `Hubs/` | GameHub cho SignalR |
| `Rules/` | GameRules và ChaosEngine |
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
`D:/Game_Project/Chesss-but-Weird/Packages/com.chessbutweird.domain/Runtime`.
Movement và balance v3 đồng bộ từ `AramRules.cs` và `AramBalanceRules.cs` của Unity.
Các tên nguồn tham chiếu
`AramBuffDefinition.cs`, `AramBuffRuntime.cs`, `AramBuffRuntime.Extended.cs`,
`AramBuffRuntime.Rifle.cs` và `ChessGame.Aram.cs` là tên legacy của Unity.
Deploy API cùng bản Unity hỗ trợ luật 3 để có đủ thao tác cho 35 buff.

Unity hiện đã dùng OnlineRestService, OnlineSession và SignalRGameTransport cho .NET; transport WebSocket cũ đã được loại bỏ. Luồng menu mới được mô tả ở mục Online menu update và tài liệu Unity liên kết bên dưới.
Swagger dùng để kiểm tra REST; các hub method cần client SignalR để gọi thủ công.

Kiểm thử engine/snapshot chạy độc lập MongoDB; Unity fixture dùng snapshot thật từ
engine với transport/account giả lập. Các kiểm tra này không thay thế trận thật hai client.

## Chạy backend

- Dùng cấu hình MongoDB/JWT hiện có. MongoDB phải là **replica set hoặc sharded cluster**
  có transaction; không hỗ trợ standalone MongoDB cho online.
- Chạy một instance API xử lý online. Lease `online_leases/runtime` chỉ cho một instance sở hữu online;
  instance mới vẫn mở HTTP và chờ lease ở background, không làm startup crash.
  Cần backplane SignalR và thiết kế phân phối presence/ownership trước khi mở rộng nhiều instance.
- Startup kiểm tra schema Chaos đã chuyển đổi, khởi tạo rating cho account chưa có ratings và tạo index.
  Startup không tự chuyển đổi Aram; xem migration offline trong tài liệu rank.
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
| AcceptSeconds | 30 |
| ReadySeconds | 180 |
| ReconnectSeconds | 60 |
| DrawOfferSeconds | 30 |
| RoomSeconds | 1800 |
| InitialRatingRange | 150 |
| RatingRangePerSecond | 10 |

## State machine và chính sách

Ticket: `Queued -> Matched / Cancelled / Expired`.
Match: `AwaitingReady -> InProgress -> Finished`; `Cancelled` chỉ áp dụng trước khi bắt đầu.
Matchmaking có bước Accept riêng: snapshot `acceptDeadline` khác null trong 30 giây và mỗi player có `accepted`. Cả hai gọi `AcceptMatch` trước deadline thì BE xóa `acceptDeadline`, phát `MatchAccepted` và bắt đầu cửa sổ Ready/setup. Một người từ chối hoặc hết hạn sẽ hủy trận, nhả cả hai seat và trả `Cancelled` với lý do `PlayerDeclined` hoặc `AcceptanceTimeout`. Phòng riêng và rematch bỏ qua bước Accept. `AcceptMatch`/`DeclineMatch` có thể gọi lại sau timeout để nhận snapshot Cancelled mà không rollback việc nhả seat.
Phòng: `Open -> Started / Closed`. Phòng có tối đa hai thành viên; chủ rời phòng thì chuyển chủ cho
người còn lại, hết thành viên thì đóng. Phòng đã start không bị đóng/rời bằng API phòng.

`online_player_seats` có `_id = userId`: một người chỉ có một chỗ trong queue, phòng hoặc trận.
Ở trong phòng chặn queue. Match creation/pairing/cancel/start cập nhật seat cùng transaction.
Nếu pairing thắng cuộc đua cancel, DELETE ticket trả `Matched` kèm `matchId`; client có thể
`DeclineMatch` nếu trận vẫn `AwaitingReady`.

Matchmaking phải được cả hai Accept trước khi `SetReady` hay gửi command setup. Match start yêu cầu cả hai `SetReady`, cả hai có kết nối hub và CHAOS hoàn tất setup nếu áp dụng.
Loadout được kiểm tra quyền sở hữu/type/item còn active tại lúc tạo và start; sau start lưu snapshot,
nên việc equip khác về sau không đổi trang bị trong trận đang chơi.

Đồng hồ đếm theo thời gian UTC của backend, tiếp tục chạy lúc mất kết nối hoặc restart.
Mỗi move hợp lệ được cộng increment; kỹ năng tiêu tốn lượt cũng được cộng increment,
kỹ năng miễn lượt không được cộng. Đồng hồ chưa chạy trong draft/setup/loading.
Hết ready/setup deadline thì hủy, không đổi Elo/thưởng. Mất toàn bộ kết nối hub của một người
thì bắt đầu grace; hết grace thua abandonment. Nếu hai deadline abandonment bằng nhau thì hòa.
Nếu timeout và abandonment cạnh tranh, deadline xảy ra trước quyết định kết quả.
Ready/setup deadline được kiểm tra trước formation fallback và trước mọi lần `StartIfReady`:
worker/recovery xử lý muộn sẽ phát `MatchCancelled`, không phát `MatchStarted` cho trận đã hết hạn.
`SetReady`/`DeclineMatch` đến sau deadline vẫn commit cancellation và giải phóng seat
trước khi trả lỗi `MatchNotActive`.

Sau restart: tải snapshot đã lưu, xử lý deadline đã hết; nếu còn chơi, đánh dấu offline và cho
grace reconnect (không kéo dài deadline reconnect đã có). Lệnh, lịch sử, outbox và kết quả vẫn ở DB.

Classic: chiếu hết, stalemate, các vị trí thiếu quân cơ bản, 5 lần lặp/75 nước tự hòa;
3 lần lặp/50 nước dùng `ClaimDraw` cho trạng thái hiện tại. Không hỗ trợ claim theo nước dự định.
CHAOS: không áp dụng draw claim/lặp/FEN; luật buff có thể tạo hoặc hủy quân và chiếu hết khác Classic.
FEN là `null` trong CHAOS; client phải dùng board + toàn bộ `chaos`.

Draw offer giữ nguyên khi người đề nghị tự đi nước; nước hợp lệ của đối thủ từ chối offer.
Không tự nhận offer của mình. Offer online có TTL `DrawOfferSeconds` (mặc định 30 giây),
là policy của game, không mô tả như quy tắc thời hạn FIDE.

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
| GET `/api/rooms?page=1&pageSize=20&mode=Classic&region=VN` | Danh sách phòng Open chưa hết hạn; filter mode/region tùy chọn; page >= 1, pageSize 1..50. Trả Page gồm roomId, code, settings, memberCount, capacity=2, expiresAt; phòng đủ người vẫn được liệt kê |
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

Mode chuẩn nhận/trả `Classic` hoặc `Chaos`; đầu vào `Aram`/`ARAM` tạm là alias của `Chaos`,
chuẩn hóa trước validation/so sánh/hash và chỉ ghi/trả Chaos. Initial 60–7200 giây, increment 0–60 giây.
MMR lấy từ `users.ratings.classic.rating` hoặc `users.ratings.chaos.rating` theo mode. Pair khi settings khớp và chênh lệch Elo nằm trong range của cả hai;
range từ 100 mở thêm 2 điểm/giây, tối đa 300. Queue/reward không dựa vào rank client gửi.
`requestId` lặp với cùng payload trả lại ticket/phòng cũ, payload khác trả `RequestIdConflict`.
Hash tạo phòng legacy được giữ nguyên và được so sánh với cả hash chuẩn lẫn hash Aram cũ;
đổi alias không làm retry hợp lệ bị conflict. Migration chuẩn hóa ticket và tính lại pool key.
Phòng lưu `CreationFingerprint` từ settings đã chuẩn hóa của request tạo phòng. Đổi settings
không thay fingerprint: tạo với A, đổi sang B, retry request A trả phòng cũ với settings B;
dùng lại requestId đó để tạo với B bị `RequestIdConflict`.
Phòng cũ thiếu fingerprint được phục hồi từ event `RoomUpdated` đầu tiên của phòng rồi lưu lại.
Nếu không xác định được request tạo phòng gốc, trả `RoomCreationRequestUnavailable` (409).
Muốn tạo lượt queue/phòng mới sau khi terminal, dùng requestId mới.
Pagination lịch sử/moves: page 1–100000, pageSize 1–100; thứ tự move tăng theo sequence. Danh sách phòng: page 1–10000, pageSize 1–50.

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
Promotion dùng `Queen`, `Rook`, `Bishop`, `Knight`; v3 Double-edged Trap chặn phong cấp
và không gửi promotion. Unity lấy lựa chọn hợp lệ từ `chaos.legalMoves` của server.
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

Events: `QueueStatusChanged`, `MatchFound`, `MatchCancelled`, `MatchAccepted`, `PlayerReadyChanged`, `MatchStarted`,
`GameStateUpdated`, `CommandRejected`, `DrawOffered`, `DrawResolved`, `PlayerConnectionChanged`,
`MatchEnded`, `RematchRequested`, `RematchResolved`, `RoomUpdated`, `RoomClosed`.
Envelope: `{eventId,type,matchId,sequence,stateVersion,serverTime,payload}`.
Event trận thường có `payload.state`; move/ability còn có `payload.commandId` và `payload.command`.
Event hết hạn draw có offerId/reason riêng. Event ticket/phòng không có match sequence.
`RoomClosed` có `payload.room` và `reason`. Rematch thành công có `state.rematchId` ở trận cũ và MatchFound trận mới.
Rematch cần đồng ý hai phía trong 2 phút; dùng loadout mới, không tính Elo cho trận rematch.

### Snapshot và reconnect không mất event

Snapshot có board (ID quân ổn định, kind/team/square/hasMoved/forward), turn, quyền nhập thành/en passant,
clocks/serverTime, stateVersion, eventSequence, ready/presence/loadout, draw offer, result và CHAOS.
Draft options của đối phương bị ẩn; buff đã chọn và hiệu ứng gameplay công khai,
ngoại trừ danh tính và cặp quân của Doppelganger v3 được che khỏi đối thủ.

Snapshot CHAOS có catalog công khai tùy chọn `chaos.buffDefinitions`:
`[{id,tier,displayName,description,summary}]` cho 35 buff v3 hoặc 26 buff v2; không lộ roll
của đối phương. `description` đầy đủ, `summary` ngắn; thiếu summary thì client
lấy câu đầu của mô tả online. Tier: Silver/Gold/Diamond/Legendary. Catalog chỉ
được thêm vào snapshot, không lưu BSON. `chaos.rulesVersion` pin theo từng trận.
`SelectBuff` vẫn được kiểm tra theo draftOptions của người gửi. Unity lưu mặc
định Full/Summary trong Settings cục bộ, chỉ áp dụng màn draft.

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

## CHAOS: ID buff, command và snapshot

Server chọn ngẫu nhiên một tier chung cho hai người; mỗi người được ba lựa chọn khác nhau trong tier đó,
chọn một buff. Mọi RNG/effect được tính trên server và lưu cùng command.
Trong AwaitingReady vẫn gọi UseAbility với expectedVersion để draft/setup; gameplay chỉ bắt đầu khi xong setup.

| ID | Buff | Cơ chế / command v3 |
| --- | --- | --- |
| 0 | CommandantPawn | `SelectTargets` với 3 pawn ID; đi thẳng hai ô trống |
| 1 | StrongFortress | Nhập thành qua chiếu/đã đi; đích an toàn, đường trống, Rook chưa đi; mỗi cánh một lần |
| 2 | FreestyleLeap | `SelectTargets` chọn một Knight được mở rộng tuyến L, không gồm chéo 2x2 |
| 3 | Doppelganger | Knight/Bishop; `ToggleSwap`/`KeepSwap`/`ReselectSwap` mỗi 4 nước chung; đối thủ thấy Gold giả |
| 4 | SuicideBomber | Queen gốc nổ một lần khi bị bắt, hủy quân không phải King trong 3x3, gồm capturer |
| 5 | FlyingThunderGod | Queen gốc tới ô trống bằng SubmitMove; tối đa 5 teleport, cooldown 5 lượt |
| 6 | NobleSacrifice | Pawn ăn Pawn mình thành Bloodthirsty; không ăn đồng minh lần hai |
| 7 | AbsoluteSniper | Bishop bắt non-Pawn từ >=4 ô lấy charge 4 nước chung; capture tiếp tự bắn không di chuyển |
| 8 | GamblingLeadsToMisery | Pawn bắt địch: 25% thêm nước, cùng Pawn, tối đa 3 lần |
| 9 | LootBox | Crate ở nước chung 25/50 bắt occupant, King chỉ miễn ở 25; `Deploy` Rook/Queen |
| 10 | PeaceTShirt | Sau 14 nước chung không bị chiếu: `Recruit` vào ba hàng đầu, có thể thay Pawn khi đầy |
| 11 | RiseOfPawn | Pawn lùi một ô trống; về hàng đáy thành mine, địch bước vào bị hủy |
| 12 | MobileFortress | `LoadPawn` trong 3x3; `UnloadPawn` từ lượt chủ tiếp theo; Rook chết thì parachute, giữ traits |
| 13 | HidingKing | Opening `HideKing` vô điều kiện; sau đó cả hai ở hàng đáy, hồi 10 nước chung |
| 14 | GachaBanner | Non-Pawn capture cho 1 ticket; `BattleGacha` tối đa 1/lượt, không thả quân gây chiếu |
| 15 | SubstituteNinjutsu | Lần đầu bị chiếu: teleport King tới ô an toàn trong nửa bàn, để decoy |
| 16 | HighTechEra | Rook gốc bắt qua đúng một blocker; charge 10 nước chung, bắn làm mới charge |
| 17 | QueensBetrayal | Mỗi nước chung 10% +5 điểm %; phản bội thành deployment của đối thủ trong 3 hàng đầu |
| 18 | DefinitionOfChaos | Sụp cặp file sau lượt chủ 10, thêm 5 nước chung sụp cặp tiếp |
| 19 | RngFiesta | Đổi quân ban đầu trừ King/Queen thành P/N/B/R ngẫu nhiên |
| 20 | IFrameRoll | Khi checkmate: `Escape` tới ô an toàn trong 5x5 quanh King, tối đa 3 lần |
| 21 | GhostArmy | Đi xuyên quân mình, không dùng xuyên để chiếu King địch |
| 22 | PlagueTown | Capturer chết sau 4 nước chung tiếp theo; King thật miễn nhiễm |
| 23 | CustomizeArmy | `ConfirmFormation` gửi toàn bộ vị trí quân trong nửa bàn; 120 giây, unchanged hoàn buff Gold |
| 24 | PawnsRevolution | Giữ King; lấp nửa bàn còn lại bằng Pawn |
| 25 | OneManArmy | Chỉ giữ King, đi thêm hai ô thẳng; `AimRifle`/`FireRifle`/`ExitRifle` |
| 26 | EveryManForHimself | Capture cùng loại P/N/B/R/Q và promote; chọn 2 Pawn thành Queen hoặc đặt 1 Queen bất kỳ nếu thiếu Pawn; hòa thành thua |
| 27 | SacUrQueen | Opening `ReplaceQueen` với `pieceKind` Rook/Bishop/Knight; sau 10 lượt chủ `RestoreQueen` chọn ally không phải King/promoted Queen |
| 28 | AutoCastling | Nhập thành phải ngay setup, đổi blocker đồng minh vào các ô bỏ trống |
| 29 | UltimateQuest | Non-Queen ăn Queen được thêm movement Queen vĩnh viễn |
| 30 | MonsterTruck | Rook đi xuyên đồng minh tới ô trống; không bắt/chiếu xuyên blocker |
| 31 | CreditCard | `BuyPiece` với `pieceKind` và `target` trong nửa bàn, 1/lượt; giá 2/4/4/5/10; được mua chịu khi balance không âm |
| 32 | KingIsPoorPiece | King thành Queen; mất cả quân, hòa, hoặc chưa thắng sau lượt chủ 25 thì thua |
| 33 | DoubleEdgedTrap | Pawn địch không promote; ở hàng cuối thành Pawn đi/bắt vuông góc 1 ô |
| 34 | Paratrooper | Sau promote, `Deploy` chọn ô trống an toàn hoặc giữ ô hiện tại cho quân vừa promote |

`combinedPlies` tăng mỗi nước đã chấp nhận, gồm bonus Pawn và sniper; setup, mua quân,
triển khai và rifle không tăng. Snapshot v3 có `legalMoves: [{from,to,promotion}]`
do server xác thực, không lưu BSON. `deployments[].action` là `Deploy`, `Redeploy`
hoặc `QuestPawns`, cùng team, pieceId, remaining và phạm vi; reconnect giữ nguyên quyết định.
V3 hòa theo 50 nước/3 lần lặp, áp dụng hòa-thành-thua của buff 26/32. Doppelganger
được che trong REST, SignalR và replay, trong khi engine vẫn tính bằng buff thật.

Ví dụ ability body:

```json
{"kind":"SelectBuff","buffId":3}
{"kind":"SelectTargets","pieceIds":[12,14]}
{"kind":"HideKing","targetPieceId":9}
{"kind":"LoadPawn","pieceId":9,"targetPieceId":17}
{"kind":"UnloadPawn","pieceId":9,"target":"c3"}
{"kind":"ReplaceQueen","pieceKind":"Rook"}
{"kind":"RestoreQueen","targetPieceId":17}
{"kind":"BuyPiece","pieceKind":"Queen","target":"c3"}
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
Nếu Gold mới là Doppelganger/Hiding King thì vẫn cần hoàn tất setup mới trước deadline.
Pending reinforcement chỉ cho chủ của deployment đầu tiên gọi Deploy; nước thường và kỹ năng khác đợi.
Snapshot có `chaos.phase`, `sides`, `deployments`, `forcedPawn`, `escapeTeam`, `mines`, `crates`,
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

Hitbox AABB canonical được snapshot trong `chaos.hitboxes`: halfWidth/halfDepth=0.3;
height King/Queen=1.2, Pawn=0.65, Rook/Bishop/Knight=0.9.
Đây là **hợp đồng hitbox online mới**, không phải số đo đã xác nhận từ mesh/FBX đang dùng.
Unity cần đồng bộ collider online và chuyển camera/ray từ world space sang hệ tọa độ này;
không dùng bounds của cosmetic mesh để quyết định rifle hit. Collider/occluder của arena mỹ thuật
không tham gia simulation; bàn và hitbox quân là hình học gameplay có thẩm quyền.

## Kết quả, Elo và thưởng

Finish lưu result và quyết định eligibility, cập nhật stats/rating/wallet/ledger đủ điều kiện trong cùng transaction.
Status Finished + index ledger `(userId,requestId,entryIndex)` chống áp dụng lần hai.
Không có endpoint nhận victory/result từ client.
Matchmaking dùng policy riêng `classic-elo-v1` hoặc `chaos-glicko2-v1`; phòng riêng/rematch không đổi rating.
**Chỉ trận matchmaking rated nhận thưởng wallet.** Phòng riêng và mọi trận tái đấu đều
`Rated=false`: Golds/Diamonds/Tickets trong result bằng 0, không cộng wallet hoặc ghi ledger thưởng,
kể cả chơi đủ thời gian/số nước. Kết quả vẫn lưu; thống kê thắng/hòa/thua chỉ cộng nếu cả hai đã có Move hợp lệ.
Backend tự đặt Rated. Rating cần nước hợp lệ của cả hai và vượt điều kiện chống farm/review.
Wallet cần thêm ≥5 Move mỗi bên và ≥60 giây; bỏ trận không nhận wallet.
Mức thưởng cho trận đủ điều kiện dùng `MatchRewardPolicy.CalculateNetworkReward` hiện có của Unity:

| Kết quả | Golds | Diamonds | Tickets |
| --- | ---: | ---: | ---: |
| Thắng | 360 | 12 | 1 |
| Hòa | 180 | 6 | 0 |
| Thua | 85 | 3 | 0 |

## Danh sách kiểm tra thủ công

1. Chạy API với replica set; login hai tài khoản có inventory hợp lệ, kết nối hub bằng hai JWT.
2. Queue cùng settings: kiểm tra hai ticket cùng matchId, màu khác nhau, current match giống nhau.
3. Matchmaking: AcceptMatch hai phía trước 30 giây rồi SetReady; phòng riêng: SetReady hai phía: kiểm tra MatchStarted và clocks; gửi nước đúng/sai lượt, nước trái luật, version cũ.
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
14. Phòng riêng: ready rồi resign ngay, tái đấu và lặp lại. Cả hai result đều có thưởng 0,
    wallet/Elo không tăng, không có ledger ONLINE_MATCH cho các trận này; matchmaking vẫn có thưởng.
15. Tạo phòng với settings A, đổi settings B, retry requestId gốc với A: trả cùng roomId và settings B.
    Retry cùng requestId với B: RequestIdConflict. Thử lại với phòng cũ thiếu CreationFingerprint.
16. Snipe Rook đang chở Pawn: Rook bị loại, bên sở hữu Rook có deployment Pawn quanh ô Rook;
    deploy đúng vùng trống 3x3 và kiểm tra promotion ở hàng cuối.
17. Formation hết hạn nhưng ready/setup chưa hết: fallback vẫn có thể bắt đầu trận.
    Worker/recovery xử lý tại hoặc sau ready/setup deadline: chỉ MatchCancelled, không MatchStarted.

## Online menu update (06/10/2026)

Unity now uses the centralized REST/SignalR services documented in
`D:/Stuurdy/Chesss-but-Weird/Documentation/OnlineDotNetIntegration.md`.
The historical Unity migration note above describes the initial backend rollout;
the legacy WebSocket backend has since been removed from this Unity checkout.

- Online opens Rooms and Matchmaking as separate choices.
- Rooms opens Create Room or Find Room. Creation immediately returns a shareable code.
- Find Room lists real open rooms with pagination and an input to join by code.
- Matchmaking opens the equipment/loadout screen. Play creates an idempotent ticket.
- Finding opponent shows elapsed search time and supports cancellation.
- MatchFound includes `acceptDeadline` and `players[].accepted`. The client must keep
  board loading and gameplay behind this gate. Accepting alone does not start the game.
- Each Accept emits `MatchAccepted` with a full versioned snapshot. The second Accept
  clears `acceptDeadline` and resets the separate 180-second Ready/setup deadline.
- The client loads the board and automatically calls SetReady when loading completes.
  CHAOS still requires its draft/formation steps before the clock starts.
- The worker enforces acceptance expiry even when neither client responds. Expiration
  and DeclineMatch cancel the whole match and free both seats; no automatic requeue.
- UI returns to loadout after failure. A new Play generates a new requestId.

Manual checks: browse and join by row/code; full, expired and closed rooms; both Accept;
only one Accept; neither Accept; Decline; cancellation versus pairing; disconnect/reconnect
while waiting; fresh Play after failure; room starts; CHAOS setup; equipped skin/board.
Only source compilation was performed for this change; no server/API/database calls or
live two-client gameplay checks were run.
## Captured prisoner presentation (October 2026)

New match snapshots include `captures`: an authoritative, cumulative array of
`{ pieceId, kind, team, capturedBy }`. `kind` and `team` describe the victim at
capture time; `capturedBy` is White or Black. Group by capturedBy and kind to
render one prisoner model plus xN per kind in six arena pads. The list is saved
with the match and included in REST state, subscription and game events, so
repeated snapshots/reconnects do not duplicate prisoners.

Classic includes normal and en-passant captures. CHAOS includes direct enemy
captures, Sniper and successful FireRifle hits. Allied sacrifices, recruited
pieces, infections, explosions, mines, pawn boarding and board collapse are
removals rather than captures and do not add prisoners. Legal-move simulations
never add entries to the actual match.

Matches created before this field was introduced return `captures: null`;
Classic clients may restore history from the move log, while CHAOS clients must
not guess captures from disappeared models. New matches return [] initially.
Deploy the updated API before validating CHAOS prisoners in Unity.

Clocks already exist: `clocks.whiteMilliseconds`, `blackMilliseconds`,
`runningColor` and `serverTime`. Values are server-adjusted remaining time;
the client renders a countdown between snapshots. The server alone decides
timeouts and increments, and the clock continues during local pause.
