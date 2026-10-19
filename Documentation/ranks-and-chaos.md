# Rank Classic/Chaos và chuyển đổi dữ liệu

Cập nhật 18/10/2026. Chỉ backend; không sửa Unity. Xác minh phiên này giới hạn ở build và source/diff review,
không chạy server, test tự động, simulation, API, MongoDB hay migration.

## Cấu hình và ý nghĩa

`ranks.json` chứa defaults server `Ranks`, version `rank-thresholds-v1`, 10 trận tính điểm riêng cho mỗi mode.
**Các ngưỡng dưới đây là đề xuất ban đầu, chưa phải ngưỡng do chủ sản phẩm chốt.**
Classic và Chaos có mảng cấu hình riêng; bảng mặc định giống nhau để khởi chạy được.
Ghi đè bằng `appsettings`/environment/command line theo cấu hình .NET; ví dụ
`Ranks__Modes__Chaos__3__MinRating=1450`. Phải tăng `Ranks__Version` khi đổi ngưỡng/metadata.
Không hot reload; rollout cùng một cấu hình cho mọi instance. ID/order là contract cố định.

| Order | tierId | displayName | minRating Classic | minRating Chaos | colorHex | badgeKey |
| --- | --- | --- | ---: | ---: | --- | --- |
| 1 | Pawn | Sắt | 0 | 0 | #747D8C | rank_pawn |
| 2 | Knight | Đồng | 1000 | 1000 | #B97847 | rank_knight |
| 3 | Bishop | Bạc | 1200 | 1200 | #BCC9D5 | rank_bishop |
| 4 | Rook | Vàng | 1400 | 1400 | #E6B541 | rank_rook |
| 5 | Queen | Bạch Kim | 1600 | 1600 | #44B9B0 | rank_queen |
| 6 | King | Kim Cương | 1800 | 1800 | #4B9AE8 | rank_king |
| 7 | Grandmaster | Tím huyền bí | 2000 | 2000 | #9461D9 | rank_grandmaster |
| 8 | Legend | Đỏ huyền thoại | 2200 | 2200 | #D95065 | rank_legend |
| 9 | Celestial | Hoàng kim thiên thể | 2400 | 2400 | #F4D68A | rank_celestial |

Không reset hay đổi rating khởi tạo 1500, rating hiện có, số trận hay peak.
Người giữ rating quanh 1500 sau trận tính điểm thứ 10 thuộc **Rook — Vàng** (1400..1599).
Pawn bao phủ 0..999; Celestial bao phủ từ 2400 trở lên, không phụ thuộc top leaderboard.
Startup validate đủ đúng 9 ID theo order 1..9, không trùng, ngưỡng tăng nghiêm ngặt,
ngưỡng đầu bằng 0 để bao phủ miền rating hợp lệ không âm, metadata hợp lệ, đủ cả hai mode và version.
Khoảng cuối không có cận trên. Giữ cách biểu diễn rating hiển thị `int` hiện có.

`RankService` dùng `RatingPolicies.Display`: làm tròn gần nhất, đúng nửa làm tròn ra xa 0.
Xét tier, rating hiển thị và pointsToNextTier cùng dùng số nguyên này: 1599.49 -> 1599/Rook;
1599.5 -> 1600/Queen. Storage/thuật toán vẫn dùng rating double, không ghi ngược số làm tròn.
Lên/hạ ngay khi vượt ngưỡng, có thể nhảy nhiều bậc. Không LP, division, mùa, reset,
thưởng mùa hay bảo vệ hạ hạng.

Trước đủ 10 ratedGames, `provisional=true`, `tierId=null` (UI hiển thị Unranked),
`placementPlayed=min(ratedGames,10)`. tierMinRating, nextTierId, nextTierMinRating,
pointsToNextTier và peakTierId đều null trong phân hạng. PeakRating vẫn có giá trị.
Sau phân hạng, peakTierId được **suy từ peak rating trọn đời theo cấu hình hiện tại**;
nó có thể đổi khi đổi ngưỡng. Không phải huy hiệu lịch sử bất biến. Peak có thể gồm rating đạt trong phân hạng.
Tier hiện tại cũng chỉ tính, không lưu vào account. Riêng tierBefore/tierAfter trong result
là snapshot tại settlement, giữ bất biến cùng rankConfigVersion.

## API và mẫu JSON

Tất cả endpoint dưới dùng Bearer JWT. Alias đầu vào Aram/ARAM (không phân biệt hoa thường,
trim khoảng trắng) chuẩn hóa thành Chaos trước validation, equality, hash và lưu.
Response chỉ dùng `mode: "Chaos"`, `chaos`; không trả key aram. BadgeKey là khóa contract mới
để Unity ánh xạ art, không khẳng định Unity đã có asset cùng tên.

### GET /api/ranks/config?mode=Chaos

Classic trả cùng cấu trúc với mode Classic và mảng ngưỡng riêng.

```json
{
  "mode": "Chaos",
  "version": "rank-thresholds-v1",
  "placementRequired": 10,
  "ratingRounding": "NearestIntegerAwayFromZero",
  "tiers": [
    {"tierId":"Pawn","order":1,"displayName":"Sắt","minRating":0,"colorHex":"#747D8C","badgeKey":"rank_pawn"},
    {"tierId":"Knight","order":2,"displayName":"Đồng","minRating":1000,"colorHex":"#B97847","badgeKey":"rank_knight"},
    {"tierId":"Bishop","order":3,"displayName":"Bạc","minRating":1200,"colorHex":"#BCC9D5","badgeKey":"rank_bishop"},
    {"tierId":"Rook","order":4,"displayName":"Vàng","minRating":1400,"colorHex":"#E6B541","badgeKey":"rank_rook"},
    {"tierId":"Queen","order":5,"displayName":"Bạch Kim","minRating":1600,"colorHex":"#44B9B0","badgeKey":"rank_queen"},
    {"tierId":"King","order":6,"displayName":"Kim Cương","minRating":1800,"colorHex":"#4B9AE8","badgeKey":"rank_king"},
    {"tierId":"Grandmaster","order":7,"displayName":"Tím huyền bí","minRating":2000,"colorHex":"#9461D9","badgeKey":"rank_grandmaster"},
    {"tierId":"Legend","order":8,"displayName":"Đỏ huyền thoại","minRating":2200,"colorHex":"#D95065","badgeKey":"rank_legend"},
    {"tierId":"Celestial","order":9,"displayName":"Hoàng kim thiên thể","minRating":2400,"colorHex":"#F4D68A","badgeKey":"rank_celestial"}
  ]
}
```

### GET /api/users/me (phần rank)

Giữ `ratings` với metadata/raw states cũ, đổi duy nhất nhánh aram -> chaos;
thêm `ranks` với dữ liệu hiển thị. Các trường profile/wallet/stats/equipped còn nguyên.
`stats.elo` vẫn là alias rating Classic hiển thị như trước.
PATCH profile trả cùng cấu trúc này, không cho client sửa rating/rank.

```json
{
  "ranks": {
    "classic": {
      "mode":"Classic","rating":1500,"tierId":"Rook","provisional":false,
      "placementPlayed":10,"placementRequired":10,"ratedGames":12,"tierMinRating":1400,
      "nextTierId":"Queen","nextTierMinRating":1600,"pointsToNextTier":100,
      "peakRating":1540,"peakTierId":"Rook","leaderboardPosition":null,"rankConfigVersion":"rank-thresholds-v1"
    },
    "chaos": {
      "mode":"Chaos","rating":1500,"tierId":null,"provisional":true,
      "placementPlayed":4,"placementRequired":10,"ratedGames":4,"tierMinRating":null,
      "nextTierId":null,"nextTierMinRating":null,"pointsToNextTier":null,
      "peakRating":1575,"peakTierId":null,"leaderboardPosition":null,"rankConfigVersion":"rank-thresholds-v1"
    }
  }
}
```

Me không tính leaderboardPosition (null). Dùng `/api/leaderboard/me?mode=...` khi cần vị trí.
Celestial đã phân hạng luôn có nextTierId/nextTierMinRating/pointsToNextTier=null rõ ràng.

### GET /api/leaderboard?mode=Chaos và /api/leaderboard/me?mode=Chaos

Top vẫn là array, me vẫn là một entry. `rank` giữ nguyên nghĩa vị trí; `leaderboardPosition`
là alias rõ nghĩa, `elo` giữ để tương thích, `rating` là alias của elo. tierId chỉ là huy hiệu.
Sắp xếp rating double giảm dần, _id tăng dần khi bằng điểm; competition ranking 1,1,3.
Me đếm người có raw rating cao hơn trong snapshot session nên cùng cách tính với top.
Hai raw rating khác nhau nhưng làm tròn ra cùng điểm có thể có vị trí khác nhau, như trước.
Mặc định loại người <10 ratedGames. `includeProvisional=true` vẫn cho phép xem vị trí của họ,
nhưng tierId vẫn null. Limit 1..100, me tìm được vị trí ngoài top 100 bằng indexed count.

```json
{
  "rank":125,"userId":"507f1f77bcf86cd799439011","username":"player","displayName":"Player","avatarId":null,
  "elo":1500,"mode":"Chaos","ratedGames":12,"deviation":120.5,"provisional":false,
  "leaderboardPosition":125,"rating":1500,"tierId":"Rook","placementPlayed":10,"placementRequired":10,
  "tierMinRating":1400,"nextTierId":"Queen","nextTierMinRating":1600,"pointsToNextTier":100,
  "peakRating":1540,"peakTierId":"Rook","rankConfigVersion":"rank-thresholds-v1"
}
```

### Result, history, snapshot và MatchEnded

`GET /api/matches/{matchId}/result`, `/api/users/me/matches`, metadata/state, và
`MatchEnded.payload.state.result` dùng cùng OfficialResult đã lưu. REST state có `chaos`,
SignalR/outbox/replay cũng chỉ trả `chaos` (đọc tương thích JSON event cũ trước khi redact draft).
Mỗi PlayerResult mới thêm tierBefore/tierAfter, provisionalBefore/provisionalAfter,
rankChangeType, rankConfigVersion, ratingApplied/ratingDecision. Root vẫn giữ quyết định rating/thưởng.

Ví dụ **một player trong result lên hạng**; số thưởng Classic thắng hiện hành:

```json
{
  "userId":"507f1f77bcf86cd799439011","ratingBefore":1590,"ratingAfter":1610,"ratingChange":20,
  "ratingDeviationAfter":null,"ratedGamesAfter":11,"tierBefore":"Rook","tierAfter":"Queen",
  "provisionalBefore":false,"provisionalAfter":false,"rankChangeType":"Promoted",
  "rankConfigVersion":"rank-thresholds-v1","ratingApplied":true,"ratingDecision":"Applied",
  "golds":360,"diamonds":12,"tickets":1
}
```

Ví dụ **hoàn tất phân hạng thứ 10** (trích PlayerResult, các trường thưởng vẫn có trong response):

```json
{
  "userId":"507f1f77bcf86cd799439011","ratingBefore":1480,"ratingAfter":1500,"ratingChange":20,
  "ratingDeviationAfter":null,"ratedGamesAfter":10,"tierBefore":null,"tierAfter":"Rook",
  "provisionalBefore":true,"provisionalAfter":false,"rankChangeType":"PlacementCompleted",
  "rankConfigVersion":"rank-thresholds-v1","ratingApplied":true,"ratingDecision":"Applied"
}
```

Ví dụ **phòng riêng không tính điểm** (trích OfficialResult với một entry minh họa;
response thật luôn có đủ hai người):

```json
{
  "ratingMode":"Chaos","ratingPolicy":"chaos-glicko2-v1","ratingApplied":false,"ratingDecision":"Unrated",
  "rewardsApplied":false,"rewardDecision":"Unrated",
  "players":[{
    "userId":"507f1f77bcf86cd799439011","ratingBefore":1500,"ratingAfter":1500,"ratingChange":0,
    "ratingDeviationAfter":120.5,"ratedGamesAfter":12,"tierBefore":"Rook","tierAfter":"Rook",
    "provisionalBefore":false,"provisionalAfter":false,"rankChangeType":"None",
    "rankConfigVersion":"rank-thresholds-v1","ratingApplied":false,"ratingDecision":"Unrated",
    "golds":0,"diamonds":0,"tickets":0
  }]
}
```

RankChangeType: None / Promoted / Demoted / PlacementCompleted. Trong phân hạng, dao động rating
vẫn None cho tới lúc hoàn tất. Kết quả trước phiên bản này có các trường snapshot mới null;
không backfill bằng config hôm nay, không giả định null nghĩa là từng có huy hiệu Unranked.
Migration giữ nguyên policy lịch sử `aram-glicko2-v1`. Kết quả mới `chaos-glicko2-v1` là **đổi tên**
của cùng Glicko-2: tau=.2, RD 60..350, cap ±75, decay theo ngày giữ nguyên. Classic Elo/K/cap giữ nguyên.

## Settlement và eligibility giữ nguyên

Hai rating sau trận được tính từ cùng hai state trước trận. Rating account, stats/colors,
wallet/ledger, result, MatchEnded outbox và nhả seat vẫn trong transaction snapshot + majority
có serialization guard và runtime lease fencing. Trạng thái terminal chặn Finish/Cancel lại;
commandId/fingerprint/ack unique giữ nguyên. Retry transaction không dùng state sau lần tính trước.
Worker/reconnect/restart không có đường cập nhật rank riêng. Event có thể giao lại; Unity dedup theo eventId/sequence.

| Trường hợp | Rating/rank |
| --- | --- |
| Matchmaking hiện có | Có tính rank nếu đủ eligibility, dù Unity đang ghi nhãn Normal |
| Phòng riêng, rematch | Không tính, Unrated; vẫn giữ xử lý stats/color history hiện có nếu đủ lượt |
| Bot/local/practice | Không có luồng cập nhật online rank |
| Chiếu hết, hòa, đầu hàng, timeout | Settlement chính thức, phải cả hai có ít nhất một Move hợp lệ |
| Một người abandonment | Có thể tính nếu cả hai đã Move và không thuộc exclusion khác; thưởng vẫn bị chặn |
| Cả hai abandonment cùng deadline | BothAbandoned, không tính |
| Một người thoát trước khi Move | NotPlayed: cả hai không đổi rating, placement, peak hoặc tier; không thêm hình phạt |
| Hủy trước start/ready/setup/accept timeout | CancelledBeforeStart: snapshot không đổi, None, không tăng ratedGames |
| Cần review | ReviewRequired, không tính rating/thưởng |
| Đã gặp cùng đối thủ 3 trận tính điểm trong 24h | RepeatedOpponentLimit; giữ cửa sổ hiện có dùng chung hai mode |
| Lỗi server/DB tạm thời | Không tự tạo kết quả có điểm; transaction rollback và xử lý lại theo luồng hiện có |

Participation đếm durable move Kind=Move; ability không được tự coi là nước đi hợp lệ cho eligibility.
Điều kiện thưởng ít nhất 5 Move mỗi người và thời gian chơi tối thiểu 60 giây là policy riêng hiện có.
Không tự thêm cơ chế gắn ReviewRequired cho mọi lỗi hay hình phạt thoát sớm.

## Migration offline, backup và rollout

Script `Migrations/chaos-v1.mongosh.js` **chỉ được chuẩn bị, chưa chạy**.
Dùng mongosh với `CHAOS_MIGRATION_DATABASE` bằng đúng `MongoDb:DatabaseName` của API
(trên Render thường là biến `MongoDb__DatabaseName`). Script bắt buộc chọn database rõ ràng,
không dựa vào database mặc định trong URI; không đặt credential trong source/log/history.
Plan mặc định chỉ đọc. Apply yêu cầu cả `CHAOS_MIGRATION_APPLY=YES` và
`CHAOS_MIGRATION_OFFLINE=YES`, từ chối nếu runtime lease chưa hết hạn.

1. Kiểm thử bản sao database trước; ghi lại số document/index, rating/ratedGames/RD/volatility/peak/lastRatedAt
   ở cả hai mode, IDs, result/policy và command ACK. Đây là công việc triển khai thủ công sau phiên code.
2. Mở cửa sổ maintenance, ngừng traffic và **dừng mọi bản API/worker, kể cả instance cũ và local**.
   Không rolling deploy trộn schema. Không xóa lease; đợi lease runtime hết hạn.
   Ưu tiên chờ trận/queue/phòng hoàn tất trước maintenance để giảm ảnh hưởng người chơi.
3. Backup nhất quán **toàn bộ database kèm index** bằng công cụ vận hành hiện có; kiểm tra có thể restore.
   Giữ bản backend/config cũ, database name và thời điểm backup. Journal không thay thế full backup.
4. Chạy plan trên DB đã dừng ghi, giải quyết mọi conflict bằng dữ liệu/audit thực tế, rồi plan lại.
   Không tự gộp hai profile khác nhau, không chọn profile theo rating cao hơn hay nhiều trận hơn.
5. Apply bằng một tiến trình migration trong maintenance. Script preflight toàn bộ collection trước khi ghi,
   đọc cursor batch 25 document. Plan chỉ giữ số liệu, không giữ bản sao toàn bộ document; có log tiến độ
   mỗi collection và mỗi 250 document, hiển thị tối đa 50 conflict kèm tổng số.
   Apply giữ kế hoạch thay đổi với giới hạn 32 MiB ước lượng chuỗi document gốc/đích (không phải giới hạn
   toàn bộ RAM process); nếu vượt giới hạn thì dừng trước mọi migration write. Database lớn cần quy trình
   vận hành phân lô có cùng preflight/journal. Khóa pool của ticket Queued vẫn được giữ để đối chiếu.
6. Nếu gián đoạn, giữ API tắt; sửa nguyên nhân và chạy lại. Marker `schema_migrations/chaos-v1`
   Running chặn backend mới khởi động. Journal `_chaos_v1_backup` lưu original của từng document đổi
   và hash đích, majority write; giữ hạn chế quyền truy cập vì có bản sao user document.
   Script kiểm tra document chưa đổi trước khi thay thế, từ chối journal không khớp.
7. Sau Completed, chạy plan đọc lại: document changes phải bằng 0, conflict=0; các index chuẩn đã tồn tại.
   So sánh backup theo checklist dưới. Startup guard kiểm tra legacy schema trước mọi startup rating/index write.
8. Deploy một backend mới cùng `ranks.json` và Unity hiểu contract Chaos. Cho worker recover hoàn tất
   rồi mở traffic. Thời gian UTC của trận/ticket/room **không được kéo dài** do migration;
   deadline có thể hết trong maintenance và sẽ được xử lý theo chính sách cũ lúc recovery.

Ví dụ PowerShell vận hành (placeholder, **không thực hiện trong phiên code**):

```powershell
# MONGODB_URI lấy an toàn từ cùng cấu hình MongoDb:ConnectionString của API.
$env:CHAOS_MIGRATION_DATABASE = '<gia-tri-MongoDb__DatabaseName-cua-API>'
# Bảo đảm lần đầu chỉ lập plan, kể cả terminal đã từng bật apply:
Remove-Item Env:CHAOS_MIGRATION_APPLY, Env:CHAOS_MIGRATION_OFFLINE -ErrorAction SilentlyContinue
mongosh $env:MONGODB_URI --file .\Migrations\chaos-v1.mongosh.js
# Chỉ sau khi có full backup, stop writers, kiểm tra plan và lease:
$env:CHAOS_MIGRATION_APPLY = 'YES'
$env:CHAOS_MIGRATION_OFFLINE = 'YES'
mongosh $env:MONGODB_URI --file .\Migrations\chaos-v1.mongosh.js
Remove-Item Env:CHAOS_MIGRATION_APPLY, Env:CHAOS_MIGRATION_OFFLINE
```

### Render build thành công nhưng startup báo legacy users

Nếu log có `Legacy mode data remains in users`, Docker build/publish đã xong nhưng
`ChaosMigrationGuard` phát hiện ít nhất một document có `ratings.aram` hoặc `onlineColors.aram`.
Đây là bước chặn schema chưa migration ở Program trước mọi startup rating/index write.
Log này chưa cho biết có dual-key conflict hay không; phải đọc kết quả plan để xác định.

Không bỏ guard, tự bật apply trong startup hay chỉ sửa mỗi users: query leaderboard, ticket pool,
room hash và match/event đang tồn tại đều phụ thuộc migration đầy đủ. Redeploy cùng dữ liệu cũ
sẽ vẫn bị chặn. Làm lần lượt quy trình maintenance/backup/plan/apply/plan phía trên rồi deploy lại.
Nếu cần quay về bản trước khi chuyển đổi dữ liệu, dùng backend/config trước tính năng Chaos/rank;
nếu migration đã ghi một phần thì phải giữ writers dừng và hoàn tất hoặc restore full backup trước.
Không chạy bản cũ trên database đã chuyển đổi dở hoặc đã chuyển sang Chaos.

Nếu Compass trắng/treo sau khi dùng wrapper `eval` trong shell nhúng, ngừng chạy lại wrapper đó.
Đóng/mở lại Compass; chỉ End task ứng dụng Compass nếu không đóng được. Không xóa cấu hình
Compass hay collection để xử lý lỗi giao diện. Lời gọi plan mặc định `apply=false` không thực hiện
migration writes; màn hình trắng tự nó không xác định được nguyên nhân hay trạng thái nếu người vận hành
đã từng gọi apply. Cần lấy log Compass trước khi kết luận. Phiên bản script đọc cursor đã được kiểm tra
cú pháp/source; chưa xác minh runtime trong Compass. Thay đổi này không chứng minh đã sửa lỗi giao diện.

Phạm vi chuyển đổi theo schema thực tế:

| Dữ liệu | Cách xử lý |
| --- | --- |
| users.ratings.aram, onlineColors.aram | Chuyển sang chaos, giữ nguyên toàn bộ object, không khởi tạo lại |
| Cả khóa cũ/mới | So sánh toàn bộ BSON/EJSON; khác bất kỳ giá trị nào dừng preflight, không ghi đè/xóa/gộp. Giống hoàn toàn thì giữ chaos, bỏ khóa trùng cũ |
| online_matches (đang chơi và lịch sử) | Settings.Mode, BSON Aram -> chaos, Result.RatingMode; board/buff ID/state/deadline/result values giữ nguyên |
| online_rooms | Settings.Mode; CreationFingerprint giữ byte-for-byte, kể cả sau room đã đổi settings |
| online_tickets (mọi trạng thái) | Settings.Mode, PoolKey SHA256 tính theo đúng serialization GameSettings chuẩn |
| online_queue_pools | Dữ liệu dẫn xuất: tạo lại từ ticket Queued, reset cursor/fairness về epoch; không đổi ticketId/seat/thời hạn. Pool cũ được journal trước khi bỏ |
| online_events.PayloadJson | Parse JSON, đổi key aram và mode/ratingMode ở snapshot/payload lồng nhau; giữ eventId/sequence/published/recipients/timestamp |
| online_moves.PayloadJson | Cùng xử lý structural JSON nếu có mode/state; gameplay command hiện tại không chứa mode |
| online_commands | Không sửa commandId, fingerprint hoặc ack: công thức hash command không có mode/settings; đổi tên enum buff vẫn giữ giá trị số |
| requestId, matchId, roomId, seat, rematchId | Không đổi |
| Index users | Tạo idx_users_leaderboard_chaos và idx_users_established_chaos (partial ratedGames>=10); bỏ hai index tên aram cũ sau khi tạo mới. Index online dùng tên field không đổi |
| Policy/results đã lưu | Giữ RatingPolicy và snapshot tier/version cũ; không settle/backfill lại |

Khi room thiếu CreationFingerprint, backend vẫn khôi phục từ event RoomUpdated tạo phòng đầu tiên;
normalization Settings thực hiện trước hash. Không tìm được nguồn tạo gốc thì vẫn trả
RoomCreationRequestUnavailable, không phỏng đoán từ settings đang sửa. Hash tạo cũ vẫn được nhận khi retry
cùng requestId ở mode Chaos/Aram; requestId với settings khác vẫn conflict.

Rollback: giữ tất cả writers dừng. Trước mở traffic, restore **toàn bộ backup + indexes trước migration**
và backend/config/Unity cũ đồng bộ, kiểm tra số liệu rồi mới mở. Journal dùng điều tra hoặc phục hồi có
kiểm soát, không có script rollback tự động xóa/gộp dữ liệu. Sau khi bản mới đã nhận dữ liệu mới,
không restore backup cũ trực tiếp vì mất các trận/account/phần thưởng phát sinh; phải freeze, backup
lần nữa và lập reverse migration có đối soát hoặc forward-fix. Không chạy binary cũ trên schema Chaos.

Legacy còn giữ có chủ đích:

- `Online/Common/ModeNormalization.cs`: tên alias, key cũ và hash tương thích.
- `Online/Infrastructure/ChaosDocumentSerializer.cs`: đọc BSON cũ vào model chuẩn, phát hiện dual conflict;
  serialize chỉ schema mới. Không thay thế migration cho truy vấn index/pool.
- `Services/Ratings/ChaosMigrationGuard.cs`: nhận diện schema cũ và chặn startup khi chưa migration.
- `Migrations/chaos-v1.mongosh.js`: nguồn field/index cũ, journal phục vụ migration/audit.
- `Services/Ratings/RatingPolicies.cs`: chú thích mapping tên policy cũ sang mới, không đổi công thức.
- Tài liệu này và ONLINE_CONTRACT: alias, rollout, tên policy lịch sử và tên file nguồn Unity legacy.
- Stored CreationFingerprint và RatingPolicy lịch sử: cố ý giữ để retry và audit; không phải schema mode mới.

## Unity cần cập nhật sau bàn giao

Gửi mode Chaos (alias Aram chỉ tạm thời); đổi DTO field snapshot/state `aram` -> `chaos`,
đọc ratings.chaos và ranks.classic/chaos. Đọc config để render tên/màu/badge/tiến độ;
map badgeKey tới asset thực tế. Hiển thị Unranked với placementPlayed/placementRequired,
ẩn next tier khi null. Dùng tierId cho huy hiệu, leaderboardPosition hoặc rank cho vị trí;
dùng RankChangeType và tier snapshot để hiển thị kết quả, xử lý history cũ snapshot null.
Không tính hay gửi điểm/bậc từ client. Ghi rõ matchmaking đang tính rank dù menu hiện dùng nhãn Normal.
Dedup MatchEnded bằng eventId/sequence, lấy lại state/result khi reconnect, không tự cộng rating.
Phiên này không chỉnh/sinh asset hay chạy Unity.

## Checklist kiểm thử thủ công sau deploy thử nghiệm

- [ ] Config cả hai mode có đúng 9 tier, version, màu, badge, placement=10; alias trả mode Chaos; invalid mode bị 400.
- [ ] Cấu hình sai thiếu/trùng ID/order, thiếu mode, ngưỡng bằng/giảm, ngưỡng đầu >0 bị từ chối startup.
- [ ] Account mới vẫn 1500 từng mode, 0/10 Unranked; thi đấu Classic không đổi Chaos và ngược lại.
- [ ] 9->10 ratedGames chỉ đổi provisional của mode đó; snapshot PlacementCompleted, tierBefore=null.
- [ ] Tại từng ngưỡng, ±1 điểm và rounding .49/.50: profile, leaderboard, result nhất quán.
- [ ] Lên/hạ nhiều bậc với cấu hình thử nghiệm sít hơn; highest Celestial có ba next fields null.
- [ ] Peak tăng/không giảm đúng; đổi config chỉ đổi tier tính từ rating/peak, không đổi snapshot lịch sử.
- [ ] Top và me cùng mode/vị trí 1,1,3 khi raw rating bằng nhau; me ngoài top100; provisional bị loại mặc định.
- [ ] Chiếu hết, hòa, đầu hàng, timeout khi đủ participation tính đúng Elo/Glicko-2 cả hai từ state trước.
- [ ] Phòng riêng, rematch giữ rating/games/peak/tier, None/Unrated; bot/local/practice không cập nhật online.
- [ ] Thoát trước Move: NotPlayed cho cả hai, không tăng placement; một/both abandonment, review, repeated opponent limit đúng policy cũ.
- [ ] Accept/ready/setup timeout và decline trước start: CancelledBeforeStart, snapshot không đổi, không thưởng.
- [ ] Retry cùng commandId/requestId, reconnect, worker retry và restart giữa settlement không tăng games/rating/thưởng lần hai.
- [ ] Hai request đồng thời cùng trận: một result/settlement, outbox có thể giao lại nhưng client không áp dụng hai lần.
- [ ] Queue Aram/ARAM/Chaos vào cùng pool, response Chaos; room browser/filter/settings, match creation, draft/buff/formation/rifle hoạt động.
- [ ] Room tạo bằng Aram, đổi settings, retry request gốc bằng Chaos vẫn thành công; reuse với settings khác vẫn conflict.
- [ ] Migration plan/apply/rerun trên bản sao: số liệu rating/games/RD/volatility/peak/lastRatedAt và color history giữ nguyên.
- [ ] Dual key khác nhau dừng trước ghi; dual key giống nhau giữ chaos đúng; journal/marker cho phép tiếp tục sau gián đoạn.
- [ ] Active match/room/ticket và event chưa publish phục hồi được, đúng board/buff/deadline, không orphan seat/pool.
- [ ] Match history/result/state/MatchEnded/replay không phát key aram; lịch sử giữ RatingPolicy cũ và tier snapshot/version cũ.
- [ ] Đối soát index và hash pool, ID/fingerprint command/creation không bị hỏng; thử rollback đầy đủ trên bản sao.

Build và source review không xác nhận transaction thực, serializer/query translation khi kết nối MongoDB,
migration trên dữ liệu thật, authenticated API, SignalR, hai client, concurrency hay Unity Game View.

Đã xác minh trong phiên code: backend Release build và project test hiện có build đều thành công
(0 warnings, 0 errors; không chạy test), `git diff --check` sạch, script qua kiểm tra cú pháp JavaScript.
Đối chiếu 7 file engine/rules/state đổi tên xác nhận chỉ thay tên định danh, giữ nguyên gameplay.
