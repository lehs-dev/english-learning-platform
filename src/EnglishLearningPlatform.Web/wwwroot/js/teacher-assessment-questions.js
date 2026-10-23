(function () {
    var form = document.getElementById("assessment-question-form");
    if (!form) return;

    var list = form.querySelector("[data-selected-list]");
    var notice = form.querySelector("[data-notice]");
    var empty = form.querySelector("[data-empty]");
    var save = form.querySelector("[data-save]");
    var addButtons = document.querySelectorAll("[data-add]");

    var dirty = false;
    var saving = false;

    // Đồng bộ nút di chuyển và chặn thêm lại câu đã chọn.
    function refresh() {
        var rows = list.querySelectorAll("[data-selected-row]");
        var ids = [];

        for (var index = 0; index < rows.length; index++) {
            var row = rows[index];
            ids.push(row.querySelector("[data-question-id]").value);

            row.querySelector("[data-points]").disabled = saving;
            row.querySelector('[data-action="up"]').disabled =
                saving || index === 0;
            row.querySelector('[data-action="down"]').disabled =
                saving || index === rows.length - 1;
            row.querySelector('[data-action="remove"]').disabled = saving;
        }

        for (var button of addButtons) {
            var selected = ids.indexOf(button.dataset.id) !== -1;
            button.disabled = saving || selected;
            button.textContent = selected ? "Đã chọn" : "Thêm";
        }

        empty.hidden = rows.length > 0;
        save.disabled = saving;
    }

    function markChanged() {
        dirty = true;
        notice.textContent = "Có thay đổi chưa lưu.";
        refresh();
    }

    // Khung HTML cố định; nội dung Question dùng textContent để tránh chèn HTML.
    function createRow(id, content) {
        var row = document.createElement("li");
        row.setAttribute("data-selected-row", "");
        row.innerHTML =
            '<input type="hidden" data-question-id />' +
            '<p class="preserve" data-content></p>' +
            '<label>Điểm ' +
                '<input type="number" data-points min="0.01" ' +
                    'max="999999.99" step="0.01" value="1" required />' +
            '</label>' +
            '<div class="actions">' +
                '<button type="button" class="button secondary" ' +
                    'data-action="up">Lên</button>' +
                '<button type="button" class="button secondary" ' +
                    'data-action="down">Xuống</button>' +
                '<button type="button" class="button secondary" ' +
                    'data-action="remove">Bỏ khỏi đề</button>' +
            '</div>';

        row.querySelector("[data-question-id]").value = id;
        row.querySelector("[data-content]").textContent = content;
        return row;
    }

    for (var button of addButtons) {
        button.addEventListener("click", function () {
            if (saving || this.disabled) return;

            var content = this.closest("tr")
                .querySelector("[data-bank-content]").textContent;

            list.appendChild(createRow(this.dataset.id, content));
            markChanged();
        });
    }

    // Sửa points cũng là thay đổi chưa lưu.
    list.addEventListener("input", function (event) {
        if (event.target.matches("[data-points]")) markChanged();
    });

    list.addEventListener("click", function (event) {
        var button = event.target.closest("[data-action]");
        if (!button || saving || button.disabled) return;

        var row = button.closest("[data-selected-row]");
        var action = button.dataset.action;

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

    // Thu thập theo thứ tự DOM, gửi JSON để points không phụ thuộc locale.
    form.addEventListener("submit", async function (event) {
        event.preventDefault();
        if (saving || !form.reportValidity()) return;

        var items = [];
        for (var row of list.querySelectorAll("[data-selected-row]")) {
            items.push({
                questionId: row.querySelector("[data-question-id]").value,
                points: Number(row.querySelector("[data-points]").value)
            });
        }

        var payload = {
            expectedUpdatedAtUtc:
                form.querySelector("[data-version]").value || null,
            items: items
        };

        saving = true;
        refresh();
        notice.textContent = "Đang lưu...";

        try {
            var response = await fetch(form.action, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json",
                    // Token thật do Form Tag Helper sinh ra.
                    "RequestVerificationToken": form.querySelector(
                        'input[name="__RequestVerificationToken"]'
                    ).value
                },
                body: JSON.stringify(payload)
            });

            if (response.redirected) {
                notice.textContent =
                    "Phiên đăng nhập đã thay đổi. " +
                    "Hãy đăng nhập ở tab khác rồi thử lại.";
                return;
            }

            var result = await response.json();
            if (!response.ok || !result.success) {
                notice.textContent =
                    result.message || "Chưa thể lưu câu hỏi.";
                notice.focus();
                return;
            }

            // GET mới lấy lại dữ liệu và version.
            dirty = false;
            saving = false;
            window.location.reload();
        } catch {
            notice.textContent =
                "Không nhận được kết quả lưu. " +
                "Danh sách đang sửa vẫn được giữ trên trang.";
        } finally {
            saving = false;
            refresh();
        }
    });

    // Không đổi bộ lọc hoặc trang khi còn dữ liệu chưa lưu.
    function guardNavigation(event) {
        if (dirty || saving) {
            event.preventDefault();
            notice.textContent = "Hãy lưu danh sách trước khi tiếp tục.";
            notice.focus();
        }
    }

    document.querySelector("[data-search-form]")
        .addEventListener("submit", guardNavigation);

    for (var link of document.querySelectorAll("[data-page]")) {
        link.addEventListener("click", guardNavigation);
    }

    window.addEventListener("beforeunload", function (event) {
        if (dirty || saving) {
            event.preventDefault();
            event.returnValue = "";
        }
    });

    refresh();
})();