(() => {
    const form = document.querySelector('[data-stimulus-editor]');
    if (!form) return;
    const passage = form.querySelector('[data-stimulus-passage]');
    const audio = form.querySelector('[data-stimulus-audio]');
    const title = form.querySelector('#Title');
    const content = form.querySelector('#ContentText');
    const fileInput = form.querySelector('#audioFile');
    const urlInput = form.querySelector('#ResourceUrl');
    const player = form.querySelector('[data-preview-audio-player]');
    const audioStatus = form.querySelector('[data-preview-audio-status]');
    const error = form.querySelector('[data-audio-error]');
    const dropzone = form.querySelector('[data-audio-dropzone]');
    let objectUrl = '';
    let currentSource = '';

    const refresh = () => {
        const isAudio = form.querySelector('[name=StimulusType]:checked')?.value === '2';
        passage.hidden = isAudio;
        passage.disabled = isAudio;
        audio.hidden = !isAudio;
        audio.disabled = !isAudio;
        content.required = !isAudio;
        form.querySelector('[data-passage-preview]').hidden = isAudio;
        form.querySelector('[data-audio-preview]').hidden = !isAudio;
        form.querySelector('[data-preview-type]').textContent = isAudio ? 'Âm thanh' : 'Bài đọc';
        form.querySelectorAll('[data-preview-title]').forEach(heading => {
            heading.textContent = title.value.trim() || (isAudio ? 'Âm thanh chưa có tiêu đề' : 'Bài đọc chưa có tiêu đề');
        });
        form.querySelector('[data-preview-passage]').textContent = content.value.trim() || 'Nội dung bài đọc sẽ xuất hiện ở đây.';
        form.querySelector('[data-passage-preview]').classList.toggle('is-empty', !content.value.trim());
        form.querySelector('[data-stimulus-tip-heading]').textContent = isAudio ? 'Sẵn sàng cho câu hỏi nghe hiểu' : 'Sẵn sàng cho câu hỏi đọc hiểu';
        const footerIcon = form.querySelector('[data-preview-footer-icon]');
        footerIcon.setAttribute('href', footerIcon.getAttribute('href').replace(/#[^#]*$/, isAudio ? '#headphones' : '#book'));

        const file = fileInput.files[0];
        const url = urlInput.value.trim();
        let validUrl = '';
        if (url) {
            try {
                const parsed = new URL(url);
                if (['http:', 'https:'].includes(parsed.protocol)) validUrl = parsed.href;
            } catch { /* Trình duyệt và máy chủ sẽ hiển thị lỗi địa chỉ. */ }
        }
        urlInput.setCustomValidity(isAudio && url && !validUrl ? 'Đường dẫn âm thanh phải là địa chỉ HTTP/HTTPS hợp lệ.' : '');
        fileInput.required = isAudio && !url;
        let fileError = '';
        if (isAudio && file) {
            if (file.size === 0 || file.size > 10 * 1024 * 1024) fileError = 'Chọn tệp có dữ liệu và không vượt quá 10 MiB.';
            else if (!/\.(mp3|wav|ogg)$/i.test(file.name)) fileError = 'Vui lòng chọn tệp MP3, WAV hoặc Ogg.';
            else if (url) fileError = 'Chỉ chọn một nguồn: tệp âm thanh hoặc đường dẫn ngoài.';
        }
        fileInput.setCustomValidity(fileError);
        error.textContent = fileError;
        form.querySelector('[data-audio-file-info]').hidden = !file;
        form.querySelector('[data-audio-file-name]').textContent = file ? `${file.name} · ${(file.size / 1024).toFixed(1)} KiB` : '';
        form.querySelector('[data-preview-audio-description]').textContent = file ? file.name : (url ? 'Âm thanh từ đường dẫn bên ngoài.' : 'Chọn tệp âm thanh để nghe thử.');
        const source = isAudio && !fileError ? (file ? objectUrl : validUrl) : '';
        player.hidden = !source;
        if (source !== currentSource) {
            player.pause();
            if (source) player.src = source;
            else player.removeAttribute('src');
            currentSource = source;
            audioStatus.textContent = '';
            player.load();
        }
    };

    form.querySelectorAll('[name=StimulusType]').forEach(radio => radio.addEventListener('change', refresh));
    [title, content, urlInput].forEach(input => input.addEventListener('input', refresh));
    const updateFile = () => {
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        objectUrl = fileInput.files[0] ? URL.createObjectURL(fileInput.files[0]) : '';
        refresh();
    };
    fileInput.addEventListener('change', updateFile);
    form.querySelector('[data-clear-audio]').addEventListener('click', () => {
        fileInput.value = '';
        updateFile();
        fileInput.focus();
    });
    ['dragenter', 'dragover'].forEach(name => dropzone.addEventListener(name, event => {
        event.preventDefault();
        dropzone.classList.add('is-dragging');
    }));
    ['dragleave', 'drop'].forEach(name => dropzone.addEventListener(name, () => dropzone.classList.remove('is-dragging')));
    dropzone.addEventListener('drop', event => {
        event.preventDefault();
        if (event.dataTransfer.files.length !== 1) {
            error.textContent = 'Vui lòng chọn một tệp âm thanh.';
            return;
        }
        fileInput.files = event.dataTransfer.files;
        updateFile();
    });
    player.addEventListener('error', () => {
        if (currentSource) audioStatus.textContent = 'Chưa thể phát âm thanh. Hãy kiểm tra tệp hoặc đường dẫn đã chọn.';
    });
    window.addEventListener('pagehide', () => { if (objectUrl) URL.revokeObjectURL(objectUrl); });
    refresh();
})();
