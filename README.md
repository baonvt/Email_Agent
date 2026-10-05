# InboxAgent backend

Dự án cá nhân dùng ASP.NET Core 10 và Clean Architecture với bốn tầng. Hiện đã có đăng ký, đăng nhập, xác nhận email, đặt lại mật khẩu và quản lý phiên bằng PostgreSQL, JWT, Redis, SendGrid.

Đã có Gmail OAuth: kết nối một hộp thư mỗi tài khoản, mã hóa/refresh token, xem trạng thái và ngắt kết nối. Làm theo [hướng dẫn tạo Google OAuth Client và thử kết nối](docs/gmail-oauth.md). Outlook, đồng bộ hộp thư, trích task, lịch và agent xử lý email là các phần phát triển tiếp theo. Frontend chưa được xây.

## Chạy bằng Docker

Cần Docker Desktop chạy Linux containers. Chạy lệnh tại thư mục chứa solution:

```powershell
Copy-Item .env.example .env
```

Điền ba secret độc lập vào `.env`: `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `JWT_SIGNING_KEY`. Với PowerShell 7, chạy lệnh sau ba lần để tạo ba giá trị; JWT cần base64 chứa ít nhất 32 byte ngẫu nhiên:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
docker compose up -d --build
```

Hoặc dùng công cụ tùy chọn `./scripts/Initialize-LocalEnvironment.ps1` thay bước copy và điền secret; cần PowerShell 7 và .NET 10 SDK. Script tạo `.env` cùng User Secrets để chạy API ngoài Docker; nếu `.env` đã có, script giữ nguyên và không sửa User Secrets.

- API: `http://localhost:8080`.
- Kiểm tra PostgreSQL/Redis: `GET /health/ready` → 200 khi kết nối được.
- OpenAPI trong Development: `/openapi/v1.json`.
- Frontend mặc định: `http://localhost:3000`; chỉnh `FRONTEND_ORIGIN` trong `.env`.
- Cổng database/cache chỉ mở trên loopback trong `docker-compose.override.yml`.

Compose có ba service: API, PostgreSQL và Redis. Named volumes giữ database, Redis AOF và Data Protection keys qua restart. `docker compose down` giữ volumes; thêm `-v` sẽ xóa dữ liệu trong chúng. `.env` và User Secrets nằm ngoài Git; `.env.example` chỉ chứa mẫu.

## Chạy API bằng IDE hoặc dotnet

Cần .NET 10 SDK. Khởi động database/cache:

```powershell
docker compose up -d postgres redis
```

Nếu chưa dùng script tạo secret, cấu hình User Secrets bằng cùng giá trị đã điền trong `.env`:

```powershell
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=127.0.0.1;Port=5432;Database=inboxagent;Username=inboxagent;Password=<POSTGRES_PASSWORD>" --project Project_AI
dotnet user-secrets set "ConnectionStrings:Redis" "127.0.0.1:6379,password=<REDIS_PASSWORD>" --project Project_AI
dotnet user-secrets set "Jwt:SigningKey" "<JWT_SIGNING_KEY>" --project Project_AI
dotnet run --project Project_AI --launch-profile http
```

API chạy tại `http://localhost:5134`; sửa host trong `Project_AI/Project_AI.http` nếu dùng file đó. Nếu đổi cổng database/cache trong `.env`, cập nhật connection string tương ứng. API chạy ngoài Docker không tự đọc `.env`; chỉnh `AuthWeb:AllowedOrigins:0` và `Email:FrontendBaseUrl` trong User Secrets nếu đổi địa chỉ frontend.

## Bật email xác nhận và reset mật khẩu

Development mặc định tắt gửi email. Register vẫn lưu email mã hóa vào outbox; tài khoản chỉ login được sau khi xác nhận email.

Cấu hình `.env` rồi tạo lại container API:

```dotenv
SENDGRID_DELIVERY_ENABLED=true
SENDGRID_API_KEY=<API key có quyền Mail Send>
SENDGRID_FROM_EMAIL=<email người gửi đã xác minh>
```

```powershell
docker compose up -d project_ai.api
```

SendGrid yêu cầu xác minh người gửi; dùng Single Sender để thử nghiệm và Domain Authentication khi triển khai thật. Xem [hướng dẫn Sender Identity](https://www.twilio.com/docs/sendgrid/for-developers/sending-email/sender-identity). Nếu API chạy ngoài Docker, đặt `Email:DeliveryEnabled`, `Email:ApiKey`, `Email:FromEmail` trong User Secrets.

Link có hạn một giờ. Worker đọc outbox mỗi hai giây, gửi tối đa tám lần có giãn thời gian retry, bỏ qua email đã quá hạn và xóa row cũ hơn một ngày khi chạy. Email có thể gửi trùng nếu worker dừng sau khi nhà cung cấp nhận mail nhưng trước khi commit database.

Link trỏ đến frontend `/auth/confirm-email` hoặc `/auth/reset-password`. Frontend cần lấy `userId`, `token` rồi POST đến API; hiện chưa có hai trang này. Khi thử bằng Postman hoặc file `.http`, lấy hai giá trị từ link nhận trong email.

## Gọi API auth

Mọi POST dưới `/api/auth` và `/api/mailboxes` cần Origin đúng allowlist và header CSRF. Postman/file `.http` khai báo:

```http
Origin: http://localhost:3000
X-InboxAgent-CSRF: 1
Content-Type: application/json
```

Browser tự gửi Origin. Ví dụ login:

```javascript
const response = await fetch('http://localhost:8080/api/auth/login', {
  method: 'POST',
  credentials: 'include',
  headers: { 'Content-Type': 'application/json', 'X-InboxAgent-CSRF': '1' },
  body: JSON.stringify({ email, password }),
});
const result = await response.json();
```

Login/refresh trả `accessToken`, `accessTokenExpiresAt`, `user`; refresh token nằm trong HttpOnly cookie. Giữ access token trong bộ nhớ và gửi `Authorization: Bearer <token>` khi gọi API cần xác thực. Refresh dùng cookie, không có body; client cần cookie jar hoặc `credentials: 'include'`. Gọi refresh tuần tự, kể cả giữa các tab: dùng lại token cũ sẽ thu hồi phiên.

| Method | Endpoint | Đầu vào / yêu cầu | Thành công |
|---|---|---|---|
| POST | `/api/auth/register` | email, password, displayName | 202 |
| POST | `/api/auth/confirm-email` | userId, token | 204 |
| POST | `/api/auth/resend-confirmation` | email | 202 |
| POST | `/api/auth/login` | email, password | 200 + cookie |
| POST | `/api/auth/refresh` | Refresh cookie | 200 + cookie mới |
| POST | `/api/auth/forgot-password` | email | 202 |
| POST | `/api/auth/reset-password` | userId, token, newPassword | 204 |
| POST | `/api/auth/logout` | Bearer | 204 |
| POST | `/api/auth/logout-all` | Bearer | 204 |
| GET | `/api/auth/me` | Bearer | 200 |
| GET | `/api/admin/status` | Bearer có role Admin | 200 |

Mật khẩu dài 12–128 ký tự, có ít nhất bốn ký tự khác nhau, chữ hoa/thường, số và ký tự đặc biệt. Sai mật khẩu năm lần khóa 15 phút. Giới hạn mặc định 20 request auth/IP/phút, chỉnh bằng `RateLimiting:AuthPermitLimit`.

Access JWT hết hạn sau 15 phút. Refresh session có hạn tuyệt đối bảy ngày, rotation không kéo dài hạn này; database chỉ lưu hash refresh token. Logout, reset và cấp Admin thu hồi session. Protected request kiểm tra cả PostgreSQL session/security stamp và Redis blacklist; Redis mất kết nối trả 503.

## Response lỗi

Lỗi dùng `application/problem+json`. Ví dụ:

```json
{
  "type": "urn:inboxagent:error:not_found",
  "title": "Not Found",
  "status": 404,
  "detail": "The requested resource was not found.",
  "instance": "/api/missing",
  "code": "not_found",
  "traceId": "request-trace-id"
}
```

Validation có thêm `errors` theo field. Các trạng thái thường gặp: 400 đầu vào sai, 401 chưa xác thực/phiên hết hạn, 403 thiếu quyền/CSRF, 404 không tìm thấy, 405 sai method, 409 xung đột, 415 sai content type, 429 quá giới hạn, 500 lỗi nội bộ, 503 dịch vụ không sẵn sàng.

`ErrorCode` định nghĩa lỗi của Application. `Responses/ResponseCodes.cs` ánh xạ sang enum có sẵn `System.Net.HttpStatusCode`, mã response và thông báo. `ApiExceptionHandler` bắt exception tập trung; lỗi server không trả chi tiết nội bộ. Response thành công giữ contract từng endpoint, 204 không có body.

## Admin, migration và cấu trúc

Register và xác nhận email trước, sau đó cấp Admin từ terminal:

```powershell
docker compose run --rm project_ai.api --promote-admin admin@example.com
```

Login lại để nhận role mới. Role User được server gán khi register; không nhận role từ request.

Development tự áp dụng migration khi startup. Thêm migration mới:

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project Project_AI.Infrastructure --startup-project Project_AI --output-dir Data/Migrations
```

Giữ package lock files trong Git; Docker restore dùng `--locked-mode`. Khi triển khai thật, cần cấu hình HTTPS/cookie/origin, SendGrid và chạy migration trong bước triển khai. Giữ và sao lưu Data Protection keys cùng database để link xác nhận/reset, outbox và token Gmail còn giải mã được.

Xem [kiến trúc và luồng auth](docs/architecture.md), [các commit refactor](docs/refactor-plan.md) và [ví dụ request](Project_AI/Project_AI.http). Kiểm thử được chạy bằng công cụ tạm rồi xóa theo yêu cầu của dự án; repo không lưu test project.
