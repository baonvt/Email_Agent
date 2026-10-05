# Kết nối Gmail — giai đoạn 1

Backend hỗ trợ một Gmail cho mỗi tài khoản InboxAgent: xin quyền đọc, lưu token mã hóa, refresh token nội bộ, xem trạng thái và ngắt kết nối. Đồng bộ email, gửi mail, Outlook, lịch và agent thuộc các giai đoạn sau.

## 1. Tạo OAuth Client trên Google Cloud

1. Mở [Google Cloud Console](https://console.cloud.google.com/), tạo/chọn project `InboxAgent`.
2. Vào **APIs & Services → Library**, tìm **Gmail API** và chọn **Enable**.
3. Vào **Google Auth Platform**, chọn **Get started** nếu chưa cấu hình. Trong **Branding**, điền tên ứng dụng, email hỗ trợ và email liên hệ.
4. Trong **Audience**, chọn **External** nếu dùng Gmail cá nhân, giữ trạng thái **Testing**, thêm Gmail sẽ kết nối vào **Test users**.
5. Trong **Data Access**, thêm `openid`, `https://www.googleapis.com/auth/userinfo.email` và `https://www.googleapis.com/auth/gmail.readonly`. Backend dùng tên rút gọn `email` cho scope thông tin email.
6. Vào **Clients → Create client**, chọn **Web application**, đặt tên `InboxAgent Local`. Trong **Authorized redirect URIs**, thêm callback bên dưới rồi chọn **Create**.
7. Lưu Client ID và Client Secret ở máy cá nhân, đặt vào `.env` hoặc User Secrets theo mục 2.

| Cách chạy | Authorized redirect URI |
|---|---|
| Docker, mặc định | `http://localhost:8080/api/mailboxes/gmail/callback` |
| dotnet/IDE, profile http | `http://localhost:5134/api/mailboxes/gmail/callback` |

Có thể đăng ký cả hai URI trên cùng client. `Gmail:RedirectUri` phải khớp chính xác URI đã đăng ký; giữ `localhost` nhất quán khi mở API. Luồng redirect ở backend này không cần Authorized JavaScript origins.

Tham khảo [cấu hình Google Auth Platform](https://developers.google.com/workspace/guides/configure-oauth-consent) và [tạo OAuth Web client](https://developers.google.com/identity/protocols/oauth2/web-server). `gmail.readonly` thuộc nhóm restricted; khi mở ứng dụng cho người dùng bên ngoài, xem [quy định scope và verification của Gmail](https://developers.google.com/workspace/gmail/api/auth/scopes).

## 2. Cấu hình riêng trên máy

Nếu chạy Docker, thêm/sửa các dòng sau trong `.env` đang có, giữ nguyên secret PostgreSQL/Redis/JWT:

```dotenv
GMAIL_ENABLED=true
GMAIL_CLIENT_ID=<Client ID vừa tạo>
GMAIL_CLIENT_SECRET=<Client Secret vừa tạo>
GMAIL_REDIRECT_URI=http://localhost:8080/api/mailboxes/gmail/callback
```

```powershell
docker compose up -d project_ai.api
```

Nếu chạy dotnet/IDE, dùng User Secrets; API không tự đọc `.env`:

```powershell
dotnet user-secrets set "Gmail:Enabled" "true" --project Project_AI
dotnet user-secrets set "Gmail:ClientId" "<Client ID>" --project Project_AI
dotnet user-secrets set "Gmail:ClientSecret" "<Client Secret>" --project Project_AI
dotnet user-secrets set "Gmail:RedirectUri" "http://localhost:5134/api/mailboxes/gmail/callback" --project Project_AI
dotnet run --project Project_AI --launch-profile http
```

`.env`, User Secrets và `client_secret*.json` không được đưa vào Git hoặc Docker image. Giữ file tải từ Google ngoài repo, không gửi secret trong chat. `.env.example` chỉ chứa mẫu trống. Khi chưa cấu hình, giữ `GMAIL_ENABLED=false`; auth vẫn hoạt động, connect trả 503 `mailbox_not_configured`.

## 3. Thử khi chưa có frontend

Đăng ký, xác nhận email InboxAgent theo README, sau đó login để lấy access token. Tài khoản InboxAgent và Gmail được kết nối có thể dùng hai địa chỉ email khác nhau.

Mở `http://localhost:8080/openapi/v1.json` trong trình duyệt, mở Developer Tools → Console và chạy đoạn sau với access token mới. Nếu chạy dotnet, mở địa chỉ tương ứng trên cổng 5134.

```javascript
const response = await fetch('/api/mailboxes/gmail/connect', {
  method: 'POST',
  credentials: 'include',
  headers: {
    Authorization: 'Bearer <ACCESS_TOKEN_INBOXAGENT>',
    'X-InboxAgent-CSRF': '1',
  },
});
const result = await response.json();
if (response.ok) location.assign(result.authorizationUrl);
else console.error(result);
```

Browser tự gửi Origin. Allowlist Development đã có localhost:8080, localhost:5134 và frontend localhost:3000. Chọn Gmail đã thêm vào Test users, cấp quyền đọc Gmail. Callback thành công chuyển đến `/api/mailboxes/gmail/result`, xóa authorization code khỏi URL và chỉ trả thông báo chung. Gọi `GET /api/mailboxes` với Bearer token để xem kết nối của mình.

POST connect phải được gọi từ **cùng trình duyệt** tiếp tục sang Google để giữ cookie HttpOnly ràng buộc OAuth. Lấy URL bằng Postman rồi mở ở trình duyệt khác sẽ thiếu cookie và bị từ chối. Trong một trình duyệt, chỉ thực hiện một luồng connect tại một thời điểm.

Request có hạn tối đa 10 phút, không vượt hạn access token InboxAgent dùng lúc bắt đầu. Nếu token hết hạn hoặc bạn logout trong lúc consent, login và bắt đầu connect lại. Callback cũ không thể ghi đè trạng thái sau khi ngắt kết nối hoặc hoàn thành một callback khác.

## 4. Endpoint và trạng thái

| Method | Endpoint | Yêu cầu / kết quả |
|---|---|---|
| GET | `/api/mailboxes` | Bearer; metadata của người gọi |
| POST | `/api/mailboxes/gmail/connect` | Bearer, Origin, CSRF; URL Google + expiresAt, cookie HttpOnly |
| GET | `/api/mailboxes/gmail/callback` | Google redirect; state + cookie hợp lệ, phiên gốc còn hiệu lực |
| GET | `/api/mailboxes/gmail/result` | Thông báo chung, không chứa dữ liệu hộp thư/token |
| POST | `/api/mailboxes/gmail/disconnect` | Bearer, Origin, CSRF; 204 |

Metadata gồm `id`, `provider`, `email`, `status`, `connectedAt`, `updatedAt`. Status gồm `disconnected`, `connected`, `requires_reconnect`. Khi connect chưa hoàn tất có thể thấy row `disconnected` với email trống.

Disconnect xóa credentials đã lưu và vô hiệu callback đang chờ bằng version của mailbox. Quyền đã cấp bên Google có thể vẫn còn; người dùng có thể thu hồi tại phần kết nối ứng dụng trong tài khoản Google. Backend chưa gọi revoke toàn bộ grant.

Token Google được mã hóa bằng Data Protection và ràng buộc theo user/mailbox. Refresh gần hạn được khóa theo mailbox trong PostgreSQL để request đồng thời không refresh trùng; Google từ chối grant chuyển trạng thái `requires_reconnect`. Redis giữ state mã hóa có TTL và GETDEL chỉ cho một callback tiêu thụ. API không trả token Google.

| Trường hợp | HTTP / code |
|---|---|
| Chưa bật/cấu hình Gmail | 503 `mailbox_not_configured` |
| Google/cache tạm không hoạt động | 503 `mailbox_unavailable` / `service_unavailable` |
| State sai, đã dùng, hết hạn hoặc sai browser | 400 `invalid_oauth_state` |
| Người dùng từ chối hoặc thiếu quyền đọc Gmail | 400 `oauth_denied` |
| Authorization code/grant không hợp lệ | 400 `oauth_rejected` |
| Phiên InboxAgent bị thu hồi/hết hạn | 401 `invalid_session` |
| Mailbox đã đổi/ngắt trong khi consent | 409 `conflict` |
| Cần cấp quyền lại | 409 `mailbox_reconnect_required` |

`redirect_uri_mismatch` trên Google thường do cổng/path không khớp client; `access_denied` có thể do Gmail chưa nằm trong Test users hoặc người dùng từ chối. Kiểm tra cấu hình rồi bắt đầu lại luồng.

## 5. Commit và kiểm tra

1. `feat: add mailbox connection schema` — entity/enum, mapping và migration PostgreSQL.
2. `feat: define mailbox authorization contracts` — DTO và interface Application.
3. `feat: implement Google OAuth client` — consent, đổi code, xác minh tài khoản Google.
4. `feat: persist encrypted mailbox credentials` — repository và bảo vệ token.
5. `feat: refresh Google access tokens` — refresh nội bộ và trạng thái cần kết nối lại.
6. `feat: implement mailbox connection flow` — use case, state Redis và phiên gốc.
7. `feat: expose Gmail connection endpoints` — controller, cookie, Origin/CSRF và mã lỗi.
8. `chore: configure local Gmail OAuth` — Docker, cấu hình local và ignore secret.
9. `docs: explain Gmail OAuth setup and flow` — hướng dẫn và ví dụ request.

Đã chạy 101 kiểm tra tạm với PostgreSQL/Redis thật và Google HTTP transport giả lập, gồm transaction, cạnh tranh callback/refresh, replay, phân quyền, logout và endpoint. Code/output test được xóa sau kiểm tra. Kết nối Gmail thật cần hoàn thành mục 1–3 trên máy của bạn; chưa được xác minh trong lần triển khai này.
