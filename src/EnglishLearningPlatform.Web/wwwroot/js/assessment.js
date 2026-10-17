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
    let expired = false;
    const dialog = form.querySelector('[data-submit-dialog]');
    const questions = [...form.querySelectorAll('[data-question]')];
    const answeredCount = () => questions.filter(question =>
        question.querySelector('input[type="radio"]:checked')?.value).length;
    const updateProgress = () => {
        const progressEl = form.querySelector('[data-answer-progress]');
        if (progressEl) progressEl.textContent = `${answeredCount()}/${questions.length} câu đã trả lời`;
        questions.forEach((question, index) => {
            const answered = Boolean(question.querySelector('input[type="radio"]:checked')?.value);
            const link = form.querySelector(`[data-question-link="${index + 1}"]`);
            if (link) {
                link.classList.toggle('is-answered', answered);
                link.setAttribute('aria-label', `Đến câu ${index + 1}, ${answered ? 'đã trả lời' : 'chưa trả lời'}`);
            }
            question.querySelectorAll('.exam-option-card').forEach(card => {
                const radio = card.querySelector('input[type="radio"]');
                card.classList.toggle('is-selected', Boolean(radio && radio.checked));
            });
        });
    };
    const send = saveOnly => {
        submitting = true;
        status.textContent = saveOnly ? 'Đang lưu đáp án…' : 'Đang nộp bài…';
        saving.finally(() => form.submit());
    };
    dialog.querySelector('[data-cancel-submit]').addEventListener('click', () => dialog.close());
    dialog.querySelector('[data-confirm-submit]').addEventListener('click', () => {
        dialog.close();
        if (!submitting) send(false);
    });
    updateProgress();

    form.addEventListener('change', event => {
        if (!event.target.matches('input[type="radio"]') || submitting) return;
        updateProgress();
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
        if (!saveOnly && !expired) {
            const unanswered = questions.length - answeredCount();
            dialog.querySelector('[data-submit-summary]').textContent = unanswered
                ? `Bạn còn ${unanswered} câu chưa trả lời. Các câu này nhận 0 điểm. Bạn muốn nộp bài?`
                : 'Bạn đã trả lời tất cả câu hỏi. Bạn muốn nộp bài?';
            dialog.showModal();
            return;
        }
        // Avoid an old autosave overwriting the choices posted by the final submission.
        send(saveOnly);
    });

    const updateClock = () => {
        const remaining = Math.max(0, Math.ceil((deadline - serverNow - (performance.now() - opened)) / 1000));
        if (!Number.isFinite(remaining)) return;
        clock.querySelector('[data-time-remaining]').textContent = `${Math.floor(remaining / 60)}:${String(remaining % 60).padStart(2, '0')}`;
        clock.classList.toggle('is-expiring', remaining <= 60);
        if (remaining === 0 && !submitting) {
            expired = true;
            if (dialog.open) dialog.close();
            form.requestSubmit(form.querySelector('[data-submit-button]'));
        }
    };
    updateClock();
    window.setInterval(updateClock, 1000);
    document.addEventListener('visibilitychange', updateClock);
})();
