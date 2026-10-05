# Kiến trúc InboxAgent

Solution giữ bốn project. Thư mục `Project_AI` chứa API, project file là `Project_AI.API.csproj`.

## Chiều phụ thuộc

```text
API            → Application, Infrastructure
Infrastructure → Application, Domain
Application    → Domain
Domain         → không phụ thuộc project khác
```

API gọi Infrastructure tại nơi đăng ký DI và khởi động database. Controller nhận `IAuthService` của Application; không truy vấn EF Core, Redis hay UserManager trực tiếp.

Application định nghĩa use case và interface cần gọi. Infrastructure triển khai chúng. Domain giữ entity và quy tắc trạng thái, không biết HTTP, Identity hay database. Request DTO dùng DataAnnotations; Application có DI abstractions để đăng ký service, không tham chiếu ASP.NET Core hoặc EF Core.

## Folder và class

| Tầng | Folder | Trách nhiệm |
|---|---|---|
| Domain | `Entities`, `Constants`, `Enums` | AuthSession, RefreshToken, MailboxConnection, tên role và trạng thái mailbox |
| Application | `Interfaces` | IAuthService, IIdentityAccountService, IAuthSessionService, IAuthEmailSender |
| Application | `DTOs/Auth`, `DTOs/Mailboxes`, `Services` | Input/output, AuthService và MailboxConnectionService điều phối use case |
| Application | `Common/Enums`, `Common/Exceptions` | ErrorCode, AppException |
| Infrastructure | `Data` | AuthDbContext, initializer, Identity/ApplicationUser, Entities/EmailOutboxMessage, Migrations |
| Infrastructure | `Repositories` | AuthSessionRepository và MailboxConnectionRepository: query/transaction PostgreSQL |
| Infrastructure | `Services` | Identity, JWT, session, Redis blacklist, tạo email, SendGrid |
| Infrastructure | `Models`, `Options` | AuthSessionData, EmailPayload và cấu hình JWT/email |
| Infrastructure | `BackgroundJobs`, `Health` | Worker gửi outbox, kiểm tra PostgreSQL/Redis |
| API | `Controllers`, `Middleware`, `Security` | HTTP endpoint, CSRF, xử lý JWT bearer |
| API | `ExceptionHandling`, `Responses`, `Options` | Bắt lỗi, ánh xạ HTTP, cấu hình browser/cookie |

Class có thể nằm ở mọi tầng; vị trí phụ thuộc vào trách nhiệm. Entity Domain có private setter và method `Revoke`, `Consume` để giữ quy tắc trạng thái. `ApplicationUser` nằm ở Infrastructure vì kế thừa Identity. DTO nhỏ dùng record để trao đổi dữ liệu.

Tên folder là quy ước của repo. Điều cần giữ là chiều phụ thuộc và việc use case không biết chi tiết database/dịch vụ ngoài.

## Interface và DependencyInjection

Giữ interface tại ranh giới Application gọi Infrastructure. Những class nội bộ Infrastructure có thể nhận nhau trực tiếp. `AuthSessionService` nhận concrete `AuthSessionRepository`; không cần đưa thêm interface repository vào Application.

Mỗi tầng có một `DependencyInjection.cs`. `Program.cs` gọi `AddApplication`, `AddInfrastructure`, `AddPresentation`; các method private chia nhóm đăng ký ngay trong file DI đó. Ví dụ:

```csharp
services.AddScoped<IAuthService, AuthService>();
services.AddScoped<IAuthSessionService, AuthSessionService>();
services.AddScoped<AuthSessionRepository>();
```

Class dùng constructor thông thường với field `private readonly`. DI tạo instance và truyền dependency qua constructor, giúp đọc rõ class đang cần gì. Đây là cách tổ chức đăng ký của dự án, không phải một tầng kiến trúc mới.

Repo không có `GlobalUsings.cs` tự viết. Mỗi file khai báo namespace dự án cần dùng; SDK vẫn bật `ImplicitUsings` cho các namespace .NET thông dụng. Không dùng generic repository, Unit of Work, CQRS hay MediatR trong base hiện tại.

## Luồng login

```text
AuthController
  → IAuthService / AuthService                  [Application]
    → IIdentityAccountService / IdentityAccountService
      → UserManager + AuthDbContext             [Infrastructure]
    → IAuthSessionService / AuthSessionService
      → TokenBlacklist                          [Redis]
      → AuthSessionRepository                   [PostgreSQL]
      → JwtTokenIssuer
```

API kiểm tra input, origin/CSRF và rate limit. Identity service kiểm tra password, lockout, email đã xác nhận. Session service tạo refresh token ngẫu nhiên, repository lưu hash cùng phiên trong transaction, JwtTokenIssuer ký access token. Controller ghi refresh cookie và trả access token/profile.

Protected request kiểm tra chữ ký, issuer, audience, hạn JWT, sau đó kiểm tra Redis blacklist và phiên/security stamp trong PostgreSQL. Controller mặc định yêu cầu xác thực qua `MapControllers().RequireAuthorization()`; endpoint công khai có `AllowAnonymous`.

## Luồng refresh và email

Refresh lấy token từ cookie → AuthService → AuthSessionService → AuthSessionRepository. Repository khóa user trước, session sau; kiểm tra hạn và token đã dùng, đánh dấu token cũ consumed rồi lưu hash mới trong cùng transaction. Nếu phát hiện token dùng lại, repository commit việc thu hồi session trước khi trả lỗi. JWT service chỉ ký access token; tạo/hash refresh token là việc của session service.

Register tạo tài khoản Identity và gán User trước khi queue email. Nếu queue thất bại, tài khoản vẫn tồn tại; resend-confirmation cho phép gửi lại. AuthEmailSender mã hóa payload vào bảng outbox. EmailOutboxWorker đọc từng row với `FOR UPDATE SKIP LOCKED`, gọi SendGrid, ghi kết quả/retry. Outbox nằm hoàn toàn trong Infrastructure, không thêm broker hoặc framework job.

AppException mang ErrorCode từ Application. API ánh xạ mã thành HTTP status và ProblemDetails; phần ứng dụng không cần biết số 404 hay 503. Dùng enum HTTP có sẵn thay vì định nghĩa lại toàn bộ mã trạng thái.

## Luồng kết nối Gmail

```text
MailboxesController                           [API: HTTP, cookie]
  → IMailboxConnectionService / MailboxConnectionService [Application]
    → IGoogleOAuthClient / GoogleOAuthClient   [Infrastructure: HTTP Google]
    → IMailboxOAuthRequestStore / MailboxOAuthRequestStore [Redis + Data Protection]
    → IMailboxConnectionStore / MailboxConnectionRepository [PostgreSQL]
```

Application tạo state/PKCE và điều phối use case qua interface. API gắn cookie HttpOnly với browser; callback dùng state một lần và kiểm tra lại phiên InboxAgent đã bắt đầu kết nối. Redis lưu request OAuth mã hóa với TTL. Repository khóa user rồi mailbox, kiểm tra owner/version trước khi lưu; logout và disconnect khiến callback cũ bị từ chối.

Domain `MailboxConnection` giữ trạng thái và method `Connect`, `Disconnect`, `RequireReconnect`. `MailboxCredential` nằm trong Infrastructure/Data/Entities vì payload mã hóa là chi tiết lưu trữ. MailboxTokenProtector và MailboxAccessTokenService là class nội bộ Infrastructure; service refresh không có endpoint trả token Google. API chỉ trả DTO metadata. Xem [hướng dẫn Gmail OAuth](gmail-oauth.md).

## Docker

`docker-compose.yml` khai báo API/PostgreSQL/Redis và volumes; override chứa cổng loopback và thiết lập Development. Dockerfile tách restore/publish/runtime, cache NuGet và chạy non-root. Cổng 8081 phục vụ profile HTTPS của Visual Studio; Compose dùng 8080.

Data Protection keys được giữ trên volume có quyền cho user chạy API. Local setup chưa mã hóa key files trên volume; khi triển khai thật cần bảo vệ và sao lưu chúng. Production cần HTTPS, cookie Secure, origin chính xác và cấu hình proxy phù hợp; rate limiter hiện nằm trong từng API instance.
