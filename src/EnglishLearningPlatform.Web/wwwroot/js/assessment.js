(() => {
    const form = document.querySelector('[data-assessment-form]');
    const clock = document.querySelector('[data-assessment-clock]');
    if (!form || !clock) return;
    const status = form.querySelector('[data-save-status]');
    const deadline = Date.parse(clock.dataset.deadline);
    const serverNow = Date.parse(clock.dataset.serverNow);
    const opened = performance.now();
    let saving = Promise.resolve();
    let submitting = false;

    form.addEventListener('change', event => {
        if (!event.target.matches('input[type="radio"]') || submitting) return;
        const data = new FormData(form);
        status.textContent = 'Đang lưu đáp án…';
        // Preserve selection order if answers change while an earlier save is in flight.
        saving = saving.then(async () => {
            if (submitting) return;
            try {
                const response = await fetch(form.dataset.saveUrl, {
                    method: 'POST', body: data, credentials: 'same-origin',
                    headers: { Accept: 'application/json' }, signal: AbortSignal.timeout(10000)
                });
                if (response.redirected) {
                    submitting = true;
                    window.location.assign(response.url);
                    return;
                }
                if (!response.ok) throw new Error('save');
                status.textContent = 'Đã lưu đáp án.';
            } catch {
                status.textContent = 'Chưa lưu được đáp án. Dùng nút Lưu đáp án hoặc Nộp bài để thử lại.';
            }
        });
    });

    form.addEventListener('submit', event => {
        event.preventDefault();
        if (submitting) return;
        const saveOnly = event.submitter?.hasAttribute('data-save-button');
        if (saveOnly) form.action = event.submitter.formAction;
        submitting = true;
        status.textContent = saveOnly ? 'Đang lưu đáp án…' : 'Đang nộp bài…';
        // Avoid an old autosave overwriting the choices posted by the final submission.
        saving.finally(() => form.submit());
    });

    const updateClock = () => {
        const remaining = Math.max(0, Math.ceil((deadline - serverNow - (performance.now() - opened)) / 1000));
        if (!Number.isFinite(remaining)) return;
        clock.querySelector('[data-time-remaining]').textContent = `${Math.floor(remaining / 60)}:${String(remaining % 60).padStart(2, '0')}`;
        clock.classList.toggle('is-expiring', remaining <= 60);
        if (remaining === 0 && !submitting) form.requestSubmit(form.querySelector('[data-submit-button]'));
    };
    updateClock();
    window.setInterval(updateClock, 1000);
    document.addEventListener('visibilitychange', updateClock);
})();
