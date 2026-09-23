# Theo dõi dự án trên GitHub

Repo: `lehs-dev/english-learning-platform`. [README](../../README.md) mô tả sản phẩm; [scope](../requirements/scope.md) ghi các quyết định cần review; [backlog.csv](backlog.csv) là **kế hoạch tham khảo** theo tuần, không phải bằng chứng đã hoàn thành. GitHub issue/PR là nơi ghi trạng thái, acceptance criteria (AC), người thực hiện và minh chứng thực tế. Bảng tiến độ môn học phải cập nhật từ các bằng chứng này.

## Cách tạo và vận hành board

Tên gợi ý: **English Learning Platform**. Nếu tạo GitHub Project, dùng Board theo `Status` và Table lọc theo `Module`/`Target week`. Board là tùy chọn; issue/PR vẫn đủ để nhóm làm việc.

| Field | Giá trị gợi ý |
| --- | --- |
| Status | Todo / In Progress / Review / Done / Blocked. |
| Priority | P0: chặn mốc bàn giao; P1: quan trọng; P2: mở rộng. |
| Module | Architecture, Identity, Learning, Question Bank, Practice, Assessment, Admin, QA, DevOps, Docs. |
| Target week | 1–13 theo lịch trong `backlog.csv`. |
| Owner | GitHub assignee là người làm chính; ghi contributor/reviewer ở issue/PR. |

Mỗi issue có: mục tiêu, actor/luồng, AC kiểm chứng được, phụ thuộc, owner, tuần/hạn, link đặc tả và rủi ro. Chia nhỏ issue quá lớn để một PR kiểm thử/review được. Bắt đầu phiên: cập nhật issue đang làm; kết thúc phiên: link commit/PR, lệnh test, trạng thái AC, blocker và người cần phản hồi. Chỉ chuyển **Done** khi AC đạt, PR được review/merge và tiến độ được cập nhật.

## Gate theo 13 tuần

| Tuần | Điều phải kiểm chứng |
| --- | --- |
| 1 (14–20/9) | Phạm vi ba môn, bốn actor, ranh giới module/rule rủi ro; ERD/kiến trúc được Khánh–Hoàng review, solution build. |
| 2 (21–27/9) | Identity, role/owner policy, EF mapping và kế hoạch migration thống nhất; kiểm tra 401/403. |
| 3–4 | Course/Module/Lesson, enrollment/progress, Question Bank và Practice/Assessment authoring bắt đầu kết nối qua API/UI. |
| 5–6 | Start/save/Resume/submit/score/result chạy xuyên suốt; test quyền, concurrency, security và CI. |
| 7 (26/10–01/11) | Demo môn Công nghệ hiện đại; bốn người trong kế hoạch vấn đáp tự setup, trace và sửa một lỗi chéo; khóa bản demo theo điều kiện được duyệt. |
| 8–9 | Recommendation, audio/Placement/skill breakdown, lifecycle/Final, clone/version và lịch sử. |
| 10–11 | Admin/reporting, audit, bảo mật và hiệu năng có test/số liệu. |
| 12–13 | UAT, tài liệu/slide/Word, dữ liệu demo, release candidate và bản nộp .NET/QLDA. |

Tên tag gợi ý sau khi gate đạt: `modern-v1.0-week7`, `dotnet-v1.0-week13`. **Không gắn tag chỉ vì đã đến ngày.**

## Repo hiện hữu và script bootstrap

`scripts/bootstrap-github.sh` phục vụ **khởi tạo repo mới**: có thể `git add`, commit, push `main`, tạo repo và issue mẫu. Không chạy nguyên script trên repo hiện tại. Tạo issue/label/Project dựa trên backlog đã chốt; dùng [Git workflow](git-workflow.md) để làm việc qua PR vào `main`.
