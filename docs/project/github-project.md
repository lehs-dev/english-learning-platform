# GitHub Project

Tên board: **English Learning Platform**.

## View

- Board by `Status`: Todo / In Progress / Done.
- Table by `Module`.
- Roadmap by `Target week` nếu tạo custom field.

## Fields gợi ý

- Status: Todo / In Progress / Review / Done.
- Priority: P0 / P1 / P2.
- Module: Architecture / Identity / Learning / Assessment / Admin / QA / DevOps / Docs.
- Target week: 1..13.
- Owner: GitHub assignee.

## Release gates

- Week 1: architecture/ERD/repo convention.
- Week 3: Auth + Learning flow.
- Week 5: end-to-end exam flow.
- Week 6: CI/test/security/hardening.
- Week 7: `modern-v1.0-week7` freeze.
- Week 13: `dotnet-v1.0-week13`.

## Bootstrap tự động

Sau khi đăng nhập GitHub CLI, chạy:

```bash
bash scripts/bootstrap-github.sh
```

Script tạo repo private, labels, issues mẫu và cố gắng tạo GitHub Project nếu token có scope `project`.
