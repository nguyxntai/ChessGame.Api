# Wallet / Economy API

Cả hai endpoint yêu cầu `Authorization: Bearer <accessToken>`.
User được xác định bằng JWT `sub`/name identifier, không nhận userId từ client.

## GET /api/wallet

Đọc số dư hiện tại từ `users.wallet`:

```json
{ "golds": 1000, "diamonds": 0, "tickets": 10 }
```

Đây là giá trị minh họa và cũng là số dư mặc định của tài khoản mới;
API luôn trả số dư thực tế đã lưu, không hardcode các giá trị này.

## GET /api/wallet/transactions?page=1&pageSize=20

Đọc lịch sử cộng/trừ từ `currency_transactions` của user đăng nhập.
`page` mặc định 1, tối thiểu 1; `pageSize` mặc định 20, từ 1 đến 100.
Offset vượt `Int32.MaxValue` bị từ chối. Sắp xếp `createdAt` giảm dần,
sau đó `_id` giảm dần để thứ tự xác định khi trùng thời gian.

```json
{
  "page": 1,
  "pageSize": 20,
  "total": 1,
  "items": [
    {
      "transactionId": "507f1f77bcf86cd799439011",
      "currency": "TICKETS",
      "amount": -1,
      "balanceAfter": 9,
      "source": "GACHA",
      "requestId": "roll-1",
      "createdAt": "2026-10-03T00:00:00Z"
    }
  ]
}
```

`amount` dương là cộng, âm là trừ; `balanceAfter` là số dư loại currency đó
sau giao dịch. Currency hiện được Gacha ghi dưới dạng `GOLDS`, `DIAMONDS`,
`TICKETS`. Lịch sử rỗng hoặc trang vượt cuối trả `items: []`.
`total` và `items` được đọc riêng; khi có giao dịch mới đồng thời, hai giá trị
có thể phản ánh thời điểm khác nhau. Phân trang bằng offset có thể dịch chuyển
giữa các request khi phát sinh giao dịch mới.

| HTTP | Ý nghĩa |
| --- | --- |
| 200 | Đọc thành công |
| 400 | Phân trang không hợp lệ (`INVALID_PAGE`), hoặc query sai kiểu |
| 401 | Token thiếu/sai/hết hạn; user ID claim thiếu/sai (`INVALID_TOKEN_USER`) |
| 403 | Tài khoản bị vô hiệu hóa (`ACCOUNT_DISABLED`) |
| 404 | Tài khoản không tồn tại (`USER_NOT_FOUND`) |
| 405 | Không hỗ trợ POST/PUT/PATCH/DELETE trên các route Wallet |

## Quyền sở hữu dữ liệu

Wallet chỉ cung cấp GET. Client không được tự sửa elo, wallet, stats hoặc
inventory. Endpoint profile hiện có chỉ cập nhật displayName/avatarId và từ
chối các trường ngoài whitelist. Trang bị skin vẫn được backend kiểm tra
quyền sở hữu. Gacha hiện có cập nhật số dư và ghi ledger trong MongoDB
transaction; API Wallet chỉ đọc các dữ liệu này.

Hai endpoint banner thuộc phần Gacha đã tồn tại trong `GachaController`:
`GET /api/gacha/banners` và `GET /api/gacha/banners/{bannerCode}`.

Index phục vụ lịch sử: `userId ASC, createdAt DESC, _id DESC`, được tạo
qua `WalletService.EnsureIndexesAsync()` lúc khởi động.

## Kiểm tra

```sh
dotnet build ChessGame.Api.csproj
dotnet test Tests/ChessGame.Api.Tests.csproj
```

Tests Wallet dùng TestServer, JWT và WalletService thật, mock MongoDB:
kiểm tra số dư, cách ly user, lịch sử cộng/trừ, thứ tự/phân trang, tài khoản
bị khóa/xóa, token lỗi và không có route sửa số dư. Không kết nối MongoDB thật.
