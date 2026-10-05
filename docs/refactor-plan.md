# Các commit tinh gọn base InboxAgent

Kế hoạch gồm sáu commit để một fresher làm một mình có thể đọc, sửa và giải thích code. Giữ Clean Architecture bốn tầng và các cơ chế auth đã triển khai.

## Lịch sử triển khai

Commit gốc `2e71165 feat: init project` được giữ nguyên. Các commit sau phản ánh việc cải thiện base đang có, không viết lại lịch sử đã push.

| Bước | Commit | Nội dung |
|---|---|---|
| 1 | `d25687c` | Ignore secret/build output, xóa test project và reference trong solution |
| 2 | `657cba4` | Sắp xếp folder/namespace, bỏ GlobalUsings tự viết, tập trung xử lý lỗi API |
| 3 | `4b05a05` | Làm rõ interface auth, bỏ interface repository không cần, thu gọn dependency |
| 4 | `b94bf37` | Constructor/readonly field rõ ràng, chia nhóm DI trong cùng file, làm dễ đọc nhánh xử lý |
| 5 | `2ee6be0` | Compose ba service, script tùy chọn, cache build, non-root và persistent keys |
| 6 | `docs: explain local setup and authentication flow` | README tiếng Việt, tài liệu kiến trúc và ví dụ request |

Bước 6 là commit chứa tài liệu này; xem hash bằng `git log --oneline -6`. Các commit được tạo local, người dùng tự push lên Git.

## Phạm vi từng commit

1. `chore: protect local secrets and remove test artifacts`: cải thiện .gitignore/.dockerignore; giữ .env.example không có secret; bỏ Project_AI.Tests.
2. `refactor: organize layers and centralize API errors`: Application dùng Interfaces/DTOs/Services; Infrastructure dùng Data/Repositories/Services; API dùng Responses/ExceptionHandling/Security. Giữ ID/schema migration cũ khi đổi namespace.
3. `refactor: simplify authentication dependencies`: đổi IIdentityAccounts/IAuthSessions thành IIdentityAccountService/IAuthSessionService; AuthSessionService nhận AuthSessionRepository concrete; AuthSessionData thuộc Infrastructure. Tạo/hash refresh token chuyển khỏi JWT issuer.
4. `refactor: make authentication and startup code easier to read`: constructor thông thường, tên dependency và cancellationToken rõ nghĩa; private method chia nhóm DI; guard/try-catch dễ theo dõi; các mốc retry có tên.
5. `refactor: streamline local Docker setup`: chỉ API/PostgreSQL/Redis; cổng loopback trong override; Data Protection keys chuẩn bị quyền trong image; thiếu secret hướng dẫn điền .env. Giữ cổng HTTPS 8081 cho Visual Studio.
6. `docs: explain local setup and authentication flow`: hướng dẫn chạy Docker/IDE, SendGrid, cookie/CSRF, endpoint/error và trách nhiệm mỗi tầng.

## Quy ước giữ lại

- Domain không phụ thuộc tầng khác; Application chỉ tham chiếu Domain.
- Interface biểu diễn dependency tại ranh giới Application. Class nội bộ Infrastructure có thể nhận concrete class.
- Class có ở mọi tầng theo trách nhiệm; entity giữ private setter và method đổi trạng thái.
- Mỗi tầng có một DependencyInjection.cs; không thêm helper file cho từng nhóm đăng ký.
- Using của namespace dự án được khai báo tại file; SDK ImplicitUsings vẫn bật.
- Không thêm generic repository, Unit of Work, CQRS, MediatR hay base service.
- Outbox hiện có nằm trong Infrastructure; giữ retry và payload mã hóa.

## Kiểm tra và giới hạn

Build Release không có warning/lỗi. Bộ kiểm tra tạm xác minh HTTP contract, migration snapshot, register/confirm/login, refresh rotation/replay, logout/reset/Admin, Redis mất kết nối và outbox với HTTP transport giả lập. Code/output test và database/cache test được xóa sau kiểm tra.

Docker được build và chạy, readiness trả 200; restart cả ba container giữ database và Data Protection keys. Chỉ dùng named volumes đang có, không xóa dữ liệu bằng down -v.

Giữ password hashing của Identity, xác nhận email, lockout/rate limit, JWT validation, refresh token hash/rotation, session revocation, Redis TTL, Origin/CSRF và role do server cấp.

Gửi SendGrid đến hộp thư thật chưa được xác minh. Sau các commit refactor, giai đoạn 1 đã bổ sung [Gmail OAuth](gmail-oauth.md), kiểm tra bằng HTTP transport giả lập; kết nối Gmail thật chờ cấu hình Google Client. Frontend xác nhận/reset, Outlook, đồng bộ inbox, lịch và agent là các phần chưa triển khai.
