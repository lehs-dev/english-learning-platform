(function () {
    var menus = document.querySelectorAll(".ta-more-actions");
    for (var menu of menus) {
        menu.addEventListener("toggle", function () {
            if (this.open) {
                for (var other of menus) if (other !== this) other.open = false;
            }
        });
    }
    document.addEventListener("click", function (event) {
        for (var menu of menus) if (!menu.contains(event.target)) menu.open = false;
    });
    document.addEventListener("keydown", function (event) {
        if (event.key !== "Escape") return;
        for (var menu of menus) {
            if (menu.open) {
                menu.open = false;
                menu.querySelector("summary").focus();
            }
        }
    });

    var form = document.querySelector("[data-configuration-form]");
    if (!form) return;
    var type = form.querySelector('[name="Input.AssessmentType"]');
    var hint = form.querySelector("[data-type-hint]");
    function updateHint() {
        var hints = {
            PlacementTest: "Đánh giá năng lực đầu vào. Có thể kết hợp nhiều kỹ năng; để trống kỹ năng mục tiêu và điểm đạt.",
            SkillAssessment: "Chọn một kỹ năng mục tiêu. Tất cả câu hỏi trong đề phải thuộc kỹ năng này.",
            PracticeExam: "Kết hợp nhiều kỹ năng để luyện thi. Để trống kỹ năng mục tiêu; điểm đạt là tùy chọn."
        };
        hint.textContent = hints[type.value] || "Chọn loại bài phù hợp với mục tiêu đánh giá của bạn.";
    }
    type.addEventListener("change", updateHint);
    updateHint();

    var initial = new URLSearchParams(new FormData(form)).toString();
    var submitting = false;
    var notice = form.querySelector("[data-configuration-notice]");
    function hasChanges() {
        return new URLSearchParams(new FormData(form)).toString() !== initial;
    }
    function guard(event) {
        if (hasChanges()) {
            event.preventDefault();
            notice.textContent = "Hãy lưu cấu hình trước khi tiếp tục.";
            notice.focus();
        }
    }
    for (var link of document.querySelectorAll("[data-configuration-navigation]")) {
        link.addEventListener("click", guard);
    }
    for (var publication of document.querySelectorAll("[data-publication-form]")) {
        publication.addEventListener("submit", guard);
    }
    form.addEventListener("submit", function () { submitting = true; });
    window.addEventListener("beforeunload", function (event) {
        if (!submitting && hasChanges()) {
            event.preventDefault();
            event.returnValue = "";
        }
    });
})();
