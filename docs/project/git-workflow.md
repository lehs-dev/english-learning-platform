# Git workflow

## Nhánh

- `main`: release.
- `develop`: tích hợp.
- `feature/123-course-crud`.
- `fix/456-attempt-timer`.

## Flow

1. Nhận issue.
2. Pull `develop`.
3. Tạo feature branch.
4. Commit nhỏ, message rõ.
5. Push + PR vào `develop`.
6. Người khác review, bắt buộc người review phải trace được logic.
7. CI xanh mới merge squash.
8. Cuối sprint/release: PR `develop -> main`.

## Commit convention gợi ý

- `feat(learning): add course enrollment`
- `fix(assessment): prevent duplicate answer`
- `test(scoring): cover fill-in-blank`
- `docs(erd): update attempt relationship`
- `ci: add test workflow`
