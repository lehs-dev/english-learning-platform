(function () {
    var form = document.getElementById("practice-question-form");

    // Practice đã khóa chỉ được xem, không gắn sự kiện chỉnh sửa.
    if (!form || form.dataset.editable !== "true") {
        return;
    }

    var list = form.querySelector("[data-selected-question-list]");
    var emptyNotice = form.querySelector("[data-empty-list]");
    var notice = form.querySelector("[data-save-notice]");
    var saveButton = form.querySelector("[data-save-button]");
    var addButtons = document.querySelectorAll("[data-add-question]");

    var dirty = false;
    var saving = false;

    // Cập nhật số thứ tự, trạng thái các nút và danh sách rỗng.
    function refresh() {
        var rows = list.querySelectorAll("[data-selected-question]");
        var selectedIds = [];

        for (var index = 0; index < rows.length; index++) {
            var row = rows[index];

            row.querySelector("[data-question-number]").textContent =
                "Câu " + (index + 1);

            selectedIds.push(
                row.querySelector('input[name="QuestionIds"]').value
            );

            row.querySelector('[data-question-action="up"]').disabled =
                saving || index === 0;

            row.querySelector('[data-question-action="down"]').disabled =
                saving || index === rows.length - 1;

            row.querySelector('[data-question-action="remove"]').disabled =
                saving;
        }

        for (var button of addButtons) {
            var alreadySelected =
                selectedIds.indexOf(button.dataset.questionId) !== -1;

            button.disabled = saving || alreadySelected;
            button.textContent = alreadySelected ? "Đã chọn" : "Thêm";
        }

        emptyNotice.hidden = rows.length > 0;
        saveButton.disabled = saving;
    }

    // Chỉ đánh dấu thay đổi trên trình duyệt, chưa ghi database.
    function markChanged() {
        dirty = true;
        notice.textContent = "Có thay đổi chưa lưu.";
        refresh();
    }

    // Tạo một dòng mới bằng khung HTML cố định.
    // Nội dung câu hỏi được gán bằng textContent để tránh chèn HTML.
    function createRow(questionId, content) {
        var row = document.createElement("li");
        row.setAttribute("data-selected-question", "");

        row.innerHTML =
            '<span data-question-number></span>' +
            '<input type="hidden" name="QuestionIds" />' +
            '<p class="preserve" data-question-text></p>' +
            '<div class="actions">' +
                '<button type="button" class="button secondary" ' +
                    'data-question-action="up">Lên</button>' +
                '<button type="button" class="button secondary" ' +
                    'data-question-action="down">Xuống</button>' +
                '<button type="button" class="button secondary" ' +
                    'data-question-action="remove">Bỏ khỏi Practice</button>' +
            '</div>';

        row.querySelector('input[name="QuestionIds"]').value = questionId;
        row.querySelector("[data-question-text]").textContent = content;

        return row;
    }

    // Thêm câu hỏi vào danh sách đang soạn, không gửi POST ngay.
    for (var button of addButtons) {
        button.addEventListener("click", function () {
            if (saving || this.disabled) {
                return;
            }

            var contentCell = this.closest("tr")
                .querySelector("[data-question-content]");

            list.appendChild(
                createRow(this.dataset.questionId, contentCell.textContent)
            );

            markChanged();
        });
    }

    // Dùng một sự kiện chung cho cả dòng cũ và dòng mới được thêm.
    list.addEventListener("click", function (event) {
        var button = event.target.closest("[data-question-action]");

        if (!button || saving || button.disabled) {
            return;
        }

        var row = button.closest("[data-selected-question]");
        var action = button.dataset.questionAction;

        if (action === "remove") {
            row.remove();
        } else if (action === "up" && row.previousElementSibling) {
            list.insertBefore(row, row.previousElementSibling);
        } else if (action === "down" && row.nextElementSibling) {
            list.insertBefore(row.nextElementSibling, row);
        } else {
            return;
        }

        markChanged();
    });

    // Lưu bằng POST nhưng không rời trang nếu server trả lỗi.
    form.addEventListener("submit", async function (event) {
        event.preventDefault();

        if (saving) {
            return;
        }

        saving = true;
        refresh();
        notice.textContent = "Đang lưu danh sách câu hỏi...";

        try {
            var response = await fetch(form.action, {
                method: "POST",
                body: new FormData(form),
                credentials: "same-origin",
                headers: {
                    "Accept": "application/json"
                }
            });

            // Phiên đăng nhập hết hạn có thể bị chuyển sang trang Login.
            // Không tự bỏ danh sách đang sửa trong trường hợp này.
            if (response.redirected) {
                notice.textContent =
                    "Phiên đăng nhập đã thay đổi. " +
                    "Hãy đăng nhập lại ở tab khác rồi thử lưu lại.";
                return;
            }

            var result = await response.json();

            if (!response.ok || !result.success) {
                notice.textContent =
                    result.message || "Chưa thể lưu danh sách câu hỏi.";
                notice.focus();
                return;
            }

            dirty = false;

            // Tải lại GET hiện tại để nhận phiên bản và dữ liệu mới.
            // Bộ lọc trong URL vẫn được giữ nguyên.
            window.location.reload();
        } catch {
            notice.textContent =
                "Không nhận được kết quả lưu. Danh sách đang sửa vẫn được giữ. " +
                "Hãy kiểm tra kết nối và thử lại.";
        } finally {
            saving = false;
            refresh();
        }
    });

    // Không Publish hoặc chuyển trang tìm kiếm khi còn thay đổi chưa lưu.
    var guardedForms = document.querySelectorAll(
        "[data-editor-navigation], [data-publication-form]"
    );

    for (var guardedForm of guardedForms) {
        guardedForm.addEventListener("submit", function (event) {
            if (dirty || saving) {
                event.preventDefault();
                notice.textContent =
                    "Hãy lưu danh sách câu hỏi thành công trước khi tiếp tục.";
                notice.focus();
            }
        });
    }

    // Cảnh báo khi đóng trang hoặc đi theo một liên kết khác.
    window.addEventListener("beforeunload", function (event) {
        if (dirty) {
            event.preventDefault();
            event.returnValue = "";
        }
    });

    refresh();
})();
