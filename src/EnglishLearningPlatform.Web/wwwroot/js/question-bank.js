(() => {
    const optionGroups = document.querySelectorAll('[data-question-options]');

    optionGroups.forEach(group => {
        if (group.dataset.locked === 'true') return;

        const list = group.querySelector('[data-option-list]');
        const addButton = group.querySelector('[data-add-option]');
        if (!list || !addButton) return;

        const renumber = () => {
            const rows = [...list.querySelectorAll('[data-option-row]')];
            rows.forEach((row, index) => {
                const input = row.querySelector('input[type="text"]');
                const label = row.querySelector('.question-option-input label');
                const radio = row.querySelector('input[type="radio"]');
                if (input) {
                    input.name = `Options[${index}].Content`;
                    input.id = `Options_${index}__Content`;
                }
                if (label) {
                    label.htmlFor = `Options_${index}__Content`;
                    label.textContent = `Lựa chọn ${index + 1}`;
                }
                if (radio) radio.value = index;

                let remove = row.querySelector('[data-remove-option]');
                if (!remove && index >= 2) {
                    remove = document.createElement('button');
                    remove.type = 'button';
                    remove.className = 'button danger small';
                    remove.dataset.removeOption = '';
                    remove.textContent = 'Xóa';
                    row.appendChild(remove);
                }
                if (remove && index < 2) remove.remove();
            });
        };

        addButton.addEventListener('click', () => {
            const index = list.querySelectorAll('[data-option-row]').length;
            const row = document.createElement('div');
            row.className = 'question-option-row';
            row.dataset.optionRow = '';
            row.innerHTML = `<div class="question-option-input"><label for="Options_${index}__Content">Lựa chọn ${index + 1}</label><input type="text" id="Options_${index}__Content" name="Options[${index}].Content" maxlength="1000" class="form-control" placeholder="Nhập nội dung lựa chọn..." /><span class="validation"></span></div><label class="check question-correct"><input type="radio" name="CorrectOptionIndex" value="${index}" /> <span>Đáp án đúng</span></label>`;
            list.appendChild(row);
            renumber();
            row.querySelector('input[type="text"]')?.focus();
        });

        list.addEventListener('click', event => {
            const remove = event.target.closest('[data-remove-option]');
            if (!remove) return;
            remove.closest('[data-option-row]')?.remove();
            renumber();
        });

        renumber();
    });
})();
