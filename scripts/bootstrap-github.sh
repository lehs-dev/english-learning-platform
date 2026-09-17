#!/usr/bin/env bash
set -euo pipefail

REPO_NAME="${REPO_NAME:-english-learning-platform}"
VISIBILITY="${VISIBILITY:-private}"
PROJECT_TITLE="${PROJECT_TITLE:-English Learning Platform}"
OWNER="${OWNER:-@me}"

command -v gh >/dev/null || { echo "Cần cài GitHub CLI (gh)."; exit 1; }
gh auth status >/dev/null || { echo "Hãy chạy: gh auth login"; exit 1; }

if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  git init -b main
fi

git add .
if ! git diff --cached --quiet; then
  git commit -m "chore: bootstrap English Learning Platform"
fi

if ! gh repo view "$REPO_NAME" >/dev/null 2>&1; then
  gh repo create "$REPO_NAME" --"$VISIBILITY" --source=. --remote=origin --push
else
  git remote get-url origin >/dev/null 2>&1 || git remote add origin "$(gh repo view "$REPO_NAME" --json url -q .url).git"
  git push -u origin main
fi

FULL_REPO="$(gh repo view "$REPO_NAME" --json nameWithOwner -q .nameWithOwner)"

echo "Repo: $FULL_REPO"

# Labels
while IFS='|' read -r name color desc; do
  gh label create "$name" --repo "$FULL_REPO" --color "$color" --description "$desc" --force >/dev/null
 done <<'EOF'
type:feature|1D76DB|Feature/user story
type:bug|D73A4A|Bug
type:infra|5319E7|Infra/CI/CD
type:docs|0075CA|Documentation
priority:P0|B60205|Blocker/MVP
priority:P1|D93F0B|Important
priority:P2|FBCA04|Nice to have
module:identity|0E8A16|Identity/Auth
module:learning|1D76DB|Course/Lesson/Progress
module:assessment|A2EEEF|Exam/Scoring
module:admin|C5DEF5|Admin/Reporting
module:devops|5319E7|CI/CD/Docker
EOF

# Create core issues if none exist with same title.
create_issue () {
  local title="$1" labels="$2" body="$3"
  if ! gh issue list --repo "$FULL_REPO" --search "\"$title\" in:title" --json title -q '.[].title' | grep -Fxq "$title"; then
    gh issue create --repo "$FULL_REPO" --title "$title" --label "$labels" --body "$body" >/dev/null
  fi
}

create_issue "W1 - Architecture, ERD and solution skeleton" "type:infra,priority:P0" "Owner: Sơn. AC: solution structure, ERD, branch rules, README, health endpoint."
create_issue "W2 - Identity, authentication and RBAC" "type:feature,module:identity,priority:P0" "Owner: Sơn. AC: Student/Teacher/Admin, login/logout, authorization boundary."
create_issue "W3 - Course, Lesson, Enrollment, Progress" "type:feature,module:learning,priority:P0" "Owner: Khánh. AC: learning vertical slice works end-to-end."
create_issue "W4 - Question Bank and Exam authoring" "type:feature,module:assessment,priority:P0" "Owner: Hoàng. AC: teacher creates questions and exam."
create_issue "W5 - Attempt, Scoring and Result" "type:feature,module:assessment,priority:P0" "Owner: Hoàng. AC: student takes exam, submits, gets score."
create_issue "W6 - Test, security, Docker and CI" "type:infra,module:devops,priority:P0" "Owner: Sơn + Khánh + Hoàng. AC: CI green, Docker build, test evidence, secret/security review."
create_issue "W7 - MVP freeze and defense rehearsal" "type:docs,priority:P0" "Owner: whole modern-tech team. AC: tag modern-v1.0-week7, random bug drill, evidence pack."
create_issue "W8 - Placement Test and skill profile" "type:feature,module:assessment,priority:P1" "Post-MVP extension."
create_issue "W10 - Admin and reporting" "type:feature,module:admin,priority:P1" "Owner: Khang + support."
create_issue "W11 - Performance and security hardening" "type:infra,module:devops,priority:P1" "Benchmark before/after; OWASP review."
create_issue "W13 - Final .NET release" "type:docs,priority:P0" "Tag dotnet-v1.0-week13; report, slide, source, demo."

# GitHub Project (best effort; needs project scope)
if gh project list --owner "$OWNER" >/dev/null 2>&1; then
  if ! gh project list --owner "$OWNER" --format json -q '.projects[].title' | grep -Fxq "$PROJECT_TITLE"; then
    gh project create --owner "$OWNER" --title "$PROJECT_TITLE" >/dev/null || true
  fi
  PROJECT_NUMBER="$(gh project list --owner "$OWNER" --format json -q ".projects[] | select(.title == \"$PROJECT_TITLE\") | .number" | head -n1)"
  if [[ -n "${PROJECT_NUMBER:-}" ]]; then
    gh issue list --repo "$FULL_REPO" --limit 100 --json url -q '.[].url' | while read -r url; do
      gh project item-add "$PROJECT_NUMBER" --owner "$OWNER" --url "$url" >/dev/null 2>&1 || true
    done
    echo "GitHub Project #$PROJECT_NUMBER đã được tạo/cập nhật."
  fi
else
  echo "Không có scope GitHub Projects. Chạy 'gh auth refresh -s project' rồi chạy lại script để tạo board."
fi

echo "Bootstrap hoàn tất. Sau đó tạo nhánh develop: git checkout -b develop && git push -u origin develop"
