(() => {
    document.querySelectorAll('[data-question-options]').forEach(group => {
        if (group.dataset.locked === 'true') return;

        const list = group.querySelector('[data-option-list]');
        const addButton = group.querySelector('[data-add-option]');
        if (!list || !addButton) return;

        const renumber = () => {
            const rows = [...list.querySelectorAll('[data-option-row]')];
            rows.forEach((row, index) => {
                const letter = index < 26 ? String.fromCharCode(65 + index) : String(index + 1);
                const input = row.querySelector('input[type="text"]');
                const label = row.querySelector('[data-option-letter]');
                const radio = row.querySelector('input[type="radio"]');
                const validation = row.querySelector('[data-valmsg-for]');
                const remove = row.querySelector('[data-remove-option]');
                input.name = `Options[${index}].Content`;
                input.id = `Options_${index}__Content`;
                label.htmlFor = input.id;
                label.textContent = letter;
                label.setAttribute('aria-label', `Lựa chọn ${letter}`);
                radio.value = index;
                radio.setAttribute('aria-label', `Chọn lựa chọn ${letter} làm đáp án đúng`);
                row.classList.toggle('is-correct', radio.checked);
                if (validation) validation.dataset.valmsgFor = input.name;
                if (remove) {
                    remove.disabled = rows.length <= 2;
                    remove.title = rows.length <= 2 ? 'Cần giữ ít nhất 2 lựa chọn' : `Xóa lựa chọn ${letter}`;
                    remove.setAttribute('aria-label', `Xóa lựa chọn ${letter}`);
                }
            });
        };

        addButton.addEventListener('click', () => {
            const row = list.querySelector('[data-option-row]')?.cloneNode(true);
            if (!row) return;
            const input = row.querySelector('input[type="text"]');
            input.value = '';
            input.defaultValue = '';
            input.classList.remove('input-validation-error');
            input.removeAttribute('aria-invalid');
            const radio = row.querySelector('input[type="radio"]');
            radio.checked = false;
            radio.defaultChecked = false;
            row.querySelectorAll('.validation').forEach(span => {
                span.textContent = '';
                span.classList.remove('field-validation-error');
                span.classList.add('field-validation-valid');
            });
            list.appendChild(row);
            renumber();
            input.focus();
        });

        list.addEventListener('change', renumber);
        list.addEventListener('click', event => {
            const remove = event.target.closest('[data-remove-option]');
            if (!remove || remove.disabled || list.children.length <= 2) return;
            const row = remove.closest('[data-option-row]');
            const focusRow = row.nextElementSibling ?? row.previousElementSibling;
            row.remove();
            renumber();
            focusRow?.querySelector('input[type="text"]')?.focus();
        });
        renumber();
    });

    document.querySelectorAll('[data-question-text-editor]').forEach(editor => {
        const textarea = editor.querySelector('[data-question-content]');
        const counter = editor.parentElement.querySelector('[data-character-count]');
        if (!textarea) return;
        const history = [textarea.value];
        let position = 0;
        const undo = editor.querySelector('[data-text-command="undo"]');
        const redo = editor.querySelector('[data-text-command="redo"]');
        const refresh = () => {
            if (counter) counter.textContent = textarea.value.length;
            if (undo) undo.disabled = position === 0;
            if (redo) redo.disabled = position === history.length - 1;
        };
        textarea.addEventListener('input', () => {
            if (history[position] !== textarea.value) {
                history.splice(position + 1);
                history.push(textarea.value);
                if (history.length > 100) history.shift();
                position = history.length - 1;
            }
            refresh();
        });
        editor.querySelectorAll('[data-text-command]').forEach(button => {
            button.addEventListener('click', () => {
                const command = button.dataset.textCommand;
                if (command === 'undo' || command === 'redo') {
                    position += command === 'undo' ? -1 : 1;
                    textarea.value = history[position];
                    textarea.dispatchEvent(new Event('input', { bubbles: true }));
                } else {
                    const start = textarea.value.lastIndexOf('\n', textarea.selectionStart - 1) + 1;
                    const selectionEnd = textarea.selectionEnd;
                    const nextLine = textarea.value.indexOf('\n', selectionEnd);
                    const end = nextLine === -1 ? textarea.value.length : nextLine;
                    const lines = textarea.value.slice(start, end).split('\n');
                    const replacement = lines.map((line, index) => {
                        const text = line.replace(/^(?:• |\d+\. )/, '');
                        return command === 'numbers' ? `${index + 1}. ${text}` : `• ${text}`;
                    }).join('\n');
                    if (textarea.maxLength >= 0 && textarea.value.length - (end - start) + replacement.length > textarea.maxLength) return;
                    textarea.setRangeText(replacement, start, end, 'select');
                    textarea.dispatchEvent(new Event('input', { bubbles: true }));
                }
                textarea.focus();
                refresh();
            });
        });
        refresh();
    });
})();
