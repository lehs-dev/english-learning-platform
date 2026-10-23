(() => {
    const editor = document.querySelector('[data-final-editor]');
    const choice = editor?.querySelector('[data-final-choice]');
    if (!choice) return;
    const card = editor.querySelector('[data-final-selection]');
    const preview = editor.querySelector('[data-selection-preview]');
    function updateSelection() {
        const option = choice.selectedOptions[0];
        if (!option) return;
        const hasChoice = Boolean(choice.value);
        const unavailable = option.dataset.unavailable === 'true';
        const changed = choice.value.toLowerCase() !== (editor.dataset.savedFinal || '').toLowerCase();
        card.hidden = false;
        card.classList.toggle('is-changed', changed);
        card.classList.toggle('is-unavailable', unavailable);
        editor.querySelector('[data-selection-state]').textContent = changed ? 'Chưa lưu' : 'Đã lưu';
        editor.querySelector('[data-selection-title]').textContent = hasChoice ? option.dataset.title : 'Không sử dụng bài kiểm tra cuối khóa';
        editor.querySelector('[data-selection-description]').textContent = unavailable
            ? 'Lựa chọn này cần được kiểm tra lại. Hãy tải lại trang hoặc chọn một đề phù hợp.'
            : hasChoice ? 'Học viên cần đạt bài kiểm tra này để hoàn thành khóa học.'
            : 'Điều kiện hoàn thành chỉ dựa trên toàn bộ bài học khả dụng.';
        editor.querySelector('[data-selection-meta]').hidden = !hasChoice || !option.dataset.kind;
        editor.querySelector('[data-selection-kind]').textContent = option.dataset.kind || '';
        editor.querySelector('[data-selection-score]').textContent = option.dataset.score ? `${option.dataset.score}%` : '';
        preview.hidden = !option.dataset.preview;
        if (option.dataset.preview) preview.setAttribute('href', option.dataset.preview);
        else preview.removeAttribute('href');
    }
    choice.addEventListener('change', updateSelection);
    window.addEventListener('pageshow', updateSelection);
    updateSelection();
})();
