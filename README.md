# InboxAgent backend

Dự án cá nhân dùng ASP.NET Core 10 và Clean Architecture với bốn tầng. Hiện đã có đăng ký, đăng nhập, xác nhận email, đặt lại mật khẩu và quản lý phiên bằng PostgreSQL, JWT, Redis, SendGrid.

Đã có Gmail OAuth, đồng bộ Inbox, Gemini phân loại/tóm tắt và soạn nháp trả lời theo hội thoại. Người dùng xem, sửa và duyệt đúng phiên bản nháp trước khi gửi Gmail. Outlook, trích task và lịch là các phần phát triển tiếp theo. Frontend chưa được xây.

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

Mọi POST, PUT và DELETE dưới `/api/auth` và `/api/mailboxes` cần Origin đúng allowlist và header CSRF. Postman/file `.http` khai báo:

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

## Phân loại và tóm tắt bằng Gemini

Tạo API key tại [Google AI Studio](https://aistudio.google.com/apikey), rồi bổ sung vào `.env` đang có; không tạo lại file nếu đã cấu hình database/Gmail:

```dotenv
GEMINI_ENABLED=true
GEMINI_API_KEY=<API key của bạn>
GEMINI_MODEL=gemini-3.5-flash-lite
```

Chạy `docker compose up -d --build project_ai.api`. Nếu chạy bằng IDE, đặt `Gemini:Enabled`, `Gemini:ApiKey`, `Gemini:Model` bằng User Secrets. Model có thể thay bằng model hỗ trợ JSON Schema trong [tài liệu Gemini](https://ai.google.dev/gemini-api/docs/models).

Sau khi login, kết nối và sync Gmail, lấy MailboxId/EmailId từ API danh sách. Các endpoint cần Bearer; POST cần thêm Origin và CSRF như phần auth:

| Method | Endpoint | Kết quả |
|---|---|---|
| POST | `/api/mailboxes/{mailboxId}/emails/{emailId}/analysis` | Phân tích hoặc dùng kết quả còn hiệu lực |
| POST | Cùng endpoint, thêm `?force=true` | Yêu cầu phân tích lại |
| GET | Cùng endpoint | Trạng thái và kết quả đã lưu, không gọi AI |

Kết quả gồm summary tiếng Việt, category (`work`, `finance`, `personal`, `promotion`, `spam`, `other`), priority (`low`, `normal`, `high`), confidence, inputTruncated, provider/model và thời gian phân tích. Confidence là ước lượng của AI. Trạng thái gồm `not_started`, `processing`, `completed`, `outdated`, `failed`; khi lần làm lại thất bại, GET có thể vẫn trả kết quả cũ nếu nội dung chưa đổi.

Gemini mặc định tắt. POST gửi nội dung email đến Google khi cần phân tích, có thể tính phí theo tài khoản API. Không có phân tích tự động hoặc tự gửi email. Input giới hạn mặc định 12000 ký tự; `inputTruncated` báo phần nội dung bị cắt. Thay đổi nội dung làm kết quả hết hiệu lực; chỉ đổi nhãn không gọi AI lại. Kết quả JSON được kiểm tra trước khi lưu, nhưng bạn vẫn cần xem lại nhận định AI.

Các lỗi riêng: 409 analysis đang chạy/nội dung đã thay đổi, 429 Gemini giới hạn request, 502 JSON không hợp lệ hoặc phản hồi không hoàn tất, 503 chưa cấu hình/Gemini không sẵn sàng. Không có retry tự động cho request AI. Xem ví dụ trong file `.http`.

## Nháp trả lời và duyệt gửi Gmail

Google OAuth hiện yêu cầu `gmail.readonly` và `gmail.send`. Thêm `https://www.googleapis.com/auth/gmail.send` ở Google Auth Platform → Data Access rồi thực hiện connect/cấp quyền lại trên cùng tài khoản Gmail, trước khi tạo nháp. Không cần disconnect trước: disconnect xóa cache email và nháp local. Mailbox chỉ có quyền đọc vẫn sync được, nhưng gửi trả `mailbox_send_permission_required`. Xem [quyền của messages.send](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/send).

| Method | Endpoint | Đầu vào |
|---|---|---|
| POST | `/api/mailboxes/{mailboxId}/emails/{emailId}/draft` | `{}` để tạo hoặc dùng nháp đã có |
| POST | Cùng endpoint | `{"force":true,"expectedVersion":"<version hiện tại>"}` để tạo lại |
| GET | `/api/mailboxes/{mailboxId}/emails/{emailId}/drafts` | Danh sách, tối đa một nháp mỗi email |
| GET | `/api/mailboxes/{mailboxId}/drafts?page=1&pageSize=20` | Nháp và lịch sử gửi trong mailbox; pageSize tối đa 50 |
| GET | `/api/mailboxes/{mailboxId}/drafts/{draftId}` | Nội dung, người nhận, version và trạng thái |
| PUT | Cùng endpoint | `{"expectedVersion":"<version>","bodyText":"<nội dung đã sửa>"}` |
| DELETE | Cùng endpoint, thêm `?expectedVersion=<version>` | Xóa nháp chưa gửi |
| POST | `/api/mailboxes/{mailboxId}/drafts/{draftId}/send` | `{"expectedVersion":"<version đã xem>","confirmSend":true}` |

Sync Inbox trước, tạo nháp, xem `from`, `to`, `subject`, `bodyText`, rồi sửa nếu cần và dùng **version mới nhất** khi duyệt. Body tối đa 10000 ký tự, gửi plain text. Subject và địa chỉ nhận chỉ đọc: backend chọn một địa chỉ Reply-To hoặc From của email gốc, không reply-all/CC/BCC/attachment. AI chỉ tạo body, mặc định cùng ngôn ngữ email và giọng lịch sự; có thể để `[placeholder]` nếu thiếu thông tin. Nháp lưu PostgreSQL, không tạo trong mục Drafts của Gmail.

Backend đọc hội thoại Gmail, lấy tối đa 20 email cho prompt và giới hạn ký tự theo `Gemini:MaxInputCharacters`. `inputTruncated` báo thiếu nội dung/ngữ cảnh. Hội thoại trên 200 message hoặc vượt giới hạn HTTP 8 MiB bị từ chối. Chưa hỗ trợ email do chính chủ gửi hoặc header/địa chỉ không hợp lệ. Khi sync đổi nội dung, nháp có `isOutdated`; re-consent cũng yêu cầu tạo nháp lại. Trước khi gửi, backend đọc lại hội thoại và chặn nếu nội dung/người nhận thay đổi. Thay đổi trên Gmail sau lần kiểm tra này vẫn có thể xảy ra vì database và Gmail không có transaction chung.

Các trạng thái: `generating`, `draft`, `generation_failed`, `sending`, `sent`, `send_unknown`. Mỗi lần sửa/tạo lại/chuyển trạng thái đổi version; request duyệt cũ trả 409. Generating giữ lease hai phút; lỗi tạo lại giữ body cũ. `sending` quá hai phút hiển thị `send_unknown`, vẫn bị khóa gửi. Response có providerMessageId khi Gmail đã xác nhận, outgoingMessageId để đối chiếu thư đã gửi.

Lặp lại yêu cầu gửi cùng phiên bản đã duyệt thành công trả kết quả lưu, không gọi Gmail lần nữa. Gemini/Gmail send không tự retry. Gmail từ chối rõ ràng (400/401/403/429) đưa nháp về `draft` với version mới để duyệt lại. Timeout, mất mạng, 5xx, phản hồi thành công không đọc được hoặc lỗi lưu sau gửi đều giữ kết quả **chưa rõ**: trả 503 `reply_send_unknown`, chặn gửi/sửa/xóa. Kiểm tra Gmail Sent; có thể tìm `rfc822msgid:<outgoingMessageId>`. Message-ID giúp đối chiếu, không phải khóa chống trùng của Gmail. Chưa có API tự khôi phục/gửi lại nháp chưa rõ kết quả.

Nếu email bị loại khỏi Inbox qua sync, nháp vẫn được giữ với `isOutdated:true` và không thể gửi khi thiếu nguồn. Disconnect xóa nháp chưa gửi và cache email, giữ bản ghi `sending`, `sent`, `send_unknown` để đối chiếu; GET nháp/lịch sử gửi vẫn đọc được khi mailbox chưa kết nối. Gmail Sent là nơi kiểm tra delivery thực tế. Logout/reset/revoke trong lúc chuẩn bị gửi được kiểm tra lại trước khi lưu approval. Chỉ endpoint có `confirmSend:true` gửi thư; AI chưa có công cụ gửi.

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

Xem [ví dụ request](Project_AI/Project_AI.http). Thư mục docs giữ hướng dẫn riêng trên máy và không được Git theo dõi. Kiểm thử được chạy bằng công cụ tạm rồi xóa theo yêu cầu của dự án; repo không lưu test project.
