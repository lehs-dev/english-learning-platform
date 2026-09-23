# Git workflow

README là hướng dẫn làm việc đầy đủ. Tài liệu này tóm tắt quy ước nhánh, PR và review đang áp dụng cho repo.

## Nhánh và CI

- `main`: nhánh tích hợp và bản chạy ổn định; chỉ nhận thay đổi qua PR.
- `feature/<issue>-<ten-ngan>`: chức năng; `fix/<issue>-<ten-ngan>`: lỗi; `docs/<issue>-<ten-ngan>`: tài liệu.
- GitHub Actions hiện chạy CI trên **PR vào `main`** và push vào `main`. Không sử dụng nhánh `develop` trong quy trình hiện tại.

## Mỗi lần nhận việc

1. Nhận issue có mục tiêu, owner, deadline và acceptance criteria. Đối chiếu đặc tả và phụ thuộc với module khác.
2. Từ thư mục repo sạch, cập nhật `main` và tạo nhánh riêng:

   ```bash
   git switch main
   git pull --ff-only origin main
   git switch -c feature/123-course-enrollment
   ```

3. Làm thay đổi nhỏ, viết test cần thiết, tự chạy `dotnet build EnglishLearningPlatform.sln` và `dotnet test EnglishLearningPlatform.sln`. Nếu thay đổi schema, thống nhất owner migration trước.
4. Kiểm tra `git diff --check`, commit rõ mục đích, push (`git push -u origin HEAD`) rồi mở PR vào `main`. Điền [PR template](../../.github/pull_request_template.md): issue, thay đổi, kết quả test, cách thử tay, migration và rủi ro liên module.
5. Thành viên khác review và CI xanh rồi mới merge. Sau merge, cập nhật issue/bảng tiến độ bằng link PR và bằng chứng nghiệm thu.

Việc chưa hoàn thành có thể để ở nhánh hoặc draft PR, nhưng phải ghi rõ blocker và bước tiếp theo vào issue; không đánh dấu Done.

`scripts/bootstrap-github.sh` được viết cho lần **khởi tạo repo**: script có `git add`, commit và push `main`, tạo repo/issue mẫu. Repo này đã tồn tại; không chạy nguyên script cho một phiên làm việc thường ngày. Tạo issue/label trên repo hiện tại theo nhu cầu thực tế.

## Commit message gợi ý

- `feat(learning): add course enrollment`
- `fix(assessment): prevent duplicate answer`
- `test(scoring): cover rounding boundary`
- `docs(erd): clarify module relationship`
- `ci: extend integration checks`

Người review cần kiểm tra đúng AC, quyền, validation, xử lý lỗi và đủ hiểu luồng để giải thích khi vấn đáp. Nếu phát hiện yêu cầu/ERD chưa thống nhất, trao đổi ngay trên issue/PR trước khi merge.
