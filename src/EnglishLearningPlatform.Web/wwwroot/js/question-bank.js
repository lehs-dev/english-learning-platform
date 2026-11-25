(() => {
    document.querySelectorAll('#question-editor-form [data-question-type]').forEach(type => {
        const form = type.form;
        const options = form.querySelector('[data-question-options]');
        const blanks = form.querySelector('[data-accepted-answers]');
        const blankList = blanks.querySelector('[data-accepted-list]');
        const structured = form.querySelector('[data-structured-section]');
        const structuredList = structured.querySelector('[data-structured-list]');
        const template = structured.querySelector('[data-structured-template]');
        const addStructured = structured.querySelector('[data-add-structured]');
        const dictationHelp = form.querySelector('[data-dictation-help]');
        const stimulus = form.querySelector('#StimulusId');
        const skill = form.querySelector('#PrimarySkill');
        const minItems = Number(structuredList.dataset.minItems);
        const maxItems = Number(structuredList.dataset.maxItems);

        const renumberBlanks = () => {
            [...blankList.children].forEach((row, index) => {
                const input = row.querySelector('input');
                input.name = `AcceptedAnswers[${index}]`;
                input.disabled = blanks.hidden;
            });
        };

        const renumberStructured = () => {
            const matching = type.value === '3';
            const rows = [...structuredList.children];
            addStructured.disabled = rows.length >= maxItems;
            rows.forEach((row, index) => {
                const left = row.querySelector('[data-structured-left]');
                const right = row.querySelector('[data-structured-right]');
                const leftLabel = row.querySelector('[data-structured-left-label]');
                const rightLabel = row.querySelector('[data-structured-right-label]');
                const remove = row.querySelector('[data-remove-structured]');
                row.classList.toggle('is-matching', matching);
                left.name = `StructuredItems[${index}].Text`;
                left.id = `structured-left-${index}`;
                leftLabel.htmlFor = left.id;
                leftLabel.textContent = `${matching ? 'Vế trái' : 'Mục'} ${index + 1}`;
                right.name = `StructuredItems[${index}].MatchText`;
                right.id = `structured-right-${index}`;
                right.disabled = !matching;
                rightLabel.htmlFor = right.id;
                rightLabel.textContent = `Vế phải ${index + 1}`;
                row.querySelector('[data-structured-match]').hidden = !matching;
                remove.disabled = rows.length <= minItems;
                remove.setAttribute('aria-label', `Xóa dòng ${index + 1}`);
            });
        };

        const updateType = () => {
            options.hidden = options.disabled = type.value !== '1';
            const dictation = type.value === '5';
            blanks.hidden = type.value !== '2' && !dictation;
            dictationHelp.hidden = !dictation;
            stimulus.required = dictation;
            for (const option of stimulus.options) {
                option.hidden = option.disabled = dictation && option.dataset.stimulusKind === 'Passage';
            }
            if (stimulus.selectedOptions[0]?.disabled) stimulus.value = '';
            if (dictation) skill.value = skill.dataset.listeningValue;
            structured.hidden = structured.disabled = type.value !== '3' && type.value !== '4';
            structured.querySelector('[data-structured-help]').textContent = type.value === '3'
                ? 'Nhập hai vế của từng cặp đúng trên cùng một dòng. Học viên sẽ thấy các vế phải đã được trộn.'
                : 'Nhập mỗi mục theo thứ tự đúng từ trên xuống. Học viên sẽ thấy các mục đã được trộn.';
            renumberBlanks();
            renumberStructured();
        };

        type.addEventListener('change', updateType);
        form.querySelector('[data-add-accepted]')?.addEventListener('click', () => {
            const row = document.createElement('div');
            row.className = 'form-group';
            row.dataset.acceptedRow = '';
            const input = document.createElement('input');
            input.type = 'text';
            input.maxLength = 500;
            input.className = 'form-control';
            input.setAttribute('aria-label', 'Đáp án được chấp nhận');
            const remove = document.createElement('button');
            remove.type = 'button';
            remove.dataset.removeAccepted = '';
            remove.textContent = 'Xóa';
            row.append(input, remove);
            blankList.append(row);
            renumberBlanks();
            input.focus();
        });
        blankList.addEventListener('click', event => {
            const button = event.target.closest('[data-remove-accepted]');
            if (!button) return;
            button.closest('[data-accepted-row]').remove();
            renumberBlanks();
        });
        addStructured.addEventListener('click', () => {
            if (structuredList.children.length >= maxItems) return;
            structuredList.append(template.content.cloneNode(true));
            renumberStructured();
            structuredList.lastElementChild.querySelector('[data-structured-left]').focus();
        });
        structuredList.addEventListener('click', event => {
            const button = event.target.closest('[data-remove-structured]');
            if (!button || structuredList.children.length <= minItems) return;
            const row = button.closest('[data-structured-row]');
            const nextFocus = row.nextElementSibling ?? row.previousElementSibling;
            row.remove();
            renumberStructured();
            nextFocus?.querySelector('[data-structured-left]')?.focus();
        });
        updateType();
    });

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
