(() => {
    const form = document.querySelector('[data-module-editor]');
    if (!form) return;
    const title = form.querySelector('[data-module-title]');
    const description = form.querySelector('[data-module-description]');
    const visibility = form.querySelector('[data-module-visibility]');
    function updatePreview() {
        form.querySelector('[data-module-title-count]').textContent = `${title.value.length}/200`;
        form.querySelector('[data-module-description-count]').textContent = `${description.value.length}/4000`;
        form.querySelector('[data-module-preview-title]').textContent = title.value.trim() || 'Tên chương học của bạn';
        form.querySelector('[data-module-preview-description]').textContent = description.value.trim() || 'Mô tả chương học sẽ hiển thị tại đây.';
        const hidden = visibility.value === '2';
        form.querySelector('[data-module-preview-status]').textContent = hidden ? 'Đã ẩn' : visibility.value === '1' ? 'Hiển thị' : 'Chưa chọn trạng thái';
        form.querySelector('.module-editor-preview').classList.toggle('is-hidden', hidden);
    }
    title.addEventListener('input', () => title.setCustomValidity(''));
    title.addEventListener('invalid', () => {
        if (title.validity.valueMissing) title.setCustomValidity('Vui lòng nhập tên chương học.');
    });
    form.addEventListener('input', updatePreview);
    visibility.addEventListener('change', updatePreview);
    window.addEventListener('pageshow', updatePreview);
    updatePreview();
})();
