document.querySelectorAll('[data-structured-question]').forEach(question => {
    const input = question.querySelector('[data-structured-answer]');
    const selects = Array.from(question.querySelectorAll('[data-structured-select]'));
    const status = question.querySelector('[data-structured-status]');
    const matching = question.dataset.structuredQuestion === 'matching';

    function restore() {
        if (!input.value) return;
        try {
            const saved = JSON.parse(input.value);
            if (!Array.isArray(saved)) return;
            selects.forEach((select, index) => {
                const value = matching ? saved.find(pair => pair.leftId === select.dataset.leftId)?.rightId : saved[index];
                if (value) select.value = value;
            });
        } catch {
            input.value = '';
        }
    }

    function reflect(notify) {
        const values = selects.map(select => select.value);
        const chosen = values.filter(Boolean);
        const duplicate = new Set(chosen).size !== chosen.length;
        let payload = '';
        if (chosen.length === values.length && !duplicate) {
            payload = JSON.stringify(matching
                ? selects.map((select, index) => ({ leftId: select.dataset.leftId, rightId: values[index] }))
                : values);
        }

        status.textContent = duplicate
            ? 'Mỗi lựa chọn chỉ dùng một lần. Câu này chưa được ghi nhận.'
            : chosen.length > 0 && !payload
                ? 'Chọn đủ các mục để câu trả lời được ghi nhận.'
                : '';

        const previouslyAnswered = input.value !== '';
        const changed = input.value !== payload;
        input.value = payload;
        if (notify && changed && (previouslyAnswered || payload)) {
            input.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    restore();
    reflect(false);
    selects.forEach(select => select.addEventListener('change', () => reflect(true)));
    question.querySelector('[data-structured-clear]').addEventListener('click', () => {
        selects.forEach(select => { select.value = ''; });
        reflect(true);
    });
});
