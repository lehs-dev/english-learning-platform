(() => {
    const expiredForm = document.querySelector('[data-expired-attempt-form]');
    if (expiredForm) {
        // Mở lại lượt quá hạn: POST có CSRF, server kiểm tra hạn và chấm đáp án đã lưu.
        // GET trang trước đó không tự sửa database và không tạo Attempt mới.
        expiredForm.submit();
        return;
    }
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
    // Mỗi câu có version riêng. Chỉ xóa khỏi dirty khi server xác nhận đúng
    // version mới nhất của chính câu đó; đáp án của tab khác không bị gửi đè.
    let changeNumber = 0;
    const versions = new Map();
    const dirtyQuestions = new Set();
    const failedQuestions = new Set();
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
    const answerIndex = question => question.querySelector('input[name$=".AssessmentQuestionId"]')
        ?.name.match(/^Answers\[([^\]]+)\]\.AssessmentQuestionId$/)?.[1];
    const updateSaveStatus = () => {
        if (submitting) return;
        status.textContent = failedQuestions.size > 0
            ? 'Chưa lưu được đáp án. Dùng nút Lưu đáp án hoặc Nộp bài để thử lại.'
            : dirtyQuestions.size > 0 ? 'Đang lưu đáp án…' : 'Đã lưu đáp án.';
    };
    const answerData = question => {
        const data = new FormData();
        const token = form.querySelector('input[name="__RequestVerificationToken"]');
        if (token) data.append(token.name, token.value);
        // MVC cần index tường minh khi chỉ gửi một câu có vị trí khác 0.
        const index = answerIndex(question);
        if (index !== undefined) data.append('Answers.Index', index);
        const questionId = question.querySelector('input[name$=".AssessmentQuestionId"]');
        if (questionId) data.append(questionId.name, questionId.value);
        const selected = question.querySelector('input[type="radio"]:checked');
        const radioName = selected?.name ?? question.querySelector('input[type="radio"]')?.name;
        // Gửi chuỗi rỗng khi bỏ chọn để model binder nhận SelectedOptionId = null.
        if (radioName) data.append(radioName, selected?.value ?? '');
        return data;
    };
    const send = saveOnly => {
        submitting = true;
        status.textContent = saveOnly ? 'Đang lưu đáp án…' : 'Đang nộp bài…';
        if (expired) status.textContent = 'Đã hết giờ. Đang chấm các đáp án đã lưu…';
        form.querySelector('[data-submit-button]').disabled = true;
        form.querySelector('[data-save-button]').disabled = true;
        if (dialog.open) {
            // Giữ modal mở khi chờ autosave để không sửa đáp án sau xác nhận.
            // Không disable radio trước khi quyết định payload: radio bị disable
            // sẽ không được gửi trong form.
            dialog.querySelector('[data-cancel-submit]').disabled = true;
            const confirmButton = dialog.querySelector('[data-confirm-submit]');
            confirmButton.disabled = true;
            confirmButton.textContent = 'Đang nộp bài…';
        }
        // Chờ request cũ kết thúc, rồi chỉ gửi các câu còn dirty.
        // Câu đã lưu thành công không được gửi lại: một tab cũ có thể giữ
        // lựa chọn cũ và ghi đè đáp án mới của tab khác.
        saving.finally(() => {
            questions.forEach((question, index) => {
                if (dirtyQuestions.has(index)) {
                    // Các câu còn dirty có thể nằm rải rác, không bắt đầu từ 0.
                    const fieldIndex = answerIndex(question);
                    if (fieldIndex !== undefined) {
                        const input = document.createElement('input');
                        input.type = 'hidden';
                        input.name = 'Answers.Index';
                        input.value = fieldIndex;
                        form.appendChild(input);
                    }
                    return;
                }
                question.querySelectorAll('input[name^="Answers["]').forEach(input => {
                    input.disabled = true;
                });
            });
            form.submit();
        });
    };
    dialog.querySelector('[data-cancel-submit]').addEventListener('click', () => {
        if (!submitting) dialog.close();
    });
    // Escape chỉ hủy xác nhận trước khi Student thực sự đồng ý nộp.
    dialog.addEventListener('cancel', event => {
        if (submitting) event.preventDefault();
    });
    dialog.querySelector('[data-confirm-submit]').addEventListener('click', () => {
        if (!submitting) send(false);
    });
    updateProgress();

    form.addEventListener('change', event => {
        if (!event.target.matches('input[type="radio"]') || submitting) return;
        const question = event.target.closest('[data-question]');
        const questionIndex = questions.indexOf(question);
        if (questionIndex < 0) return;
        updateProgress();
        const currentChange = ++changeNumber;
        versions.set(questionIndex, currentChange);
        dirtyQuestions.add(questionIndex);
        failedQuestions.delete(questionIndex);
        const data = answerData(question);
        updateSaveStatus();
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
                const result = await response.json();
                if (!result || result.saved !== true) throw new Error('save');

                // Chỉ đánh dấu sạch khi câu này chưa bị đổi tiếp trong lúc chờ.
                if (versions.get(questionIndex) === currentChange) {
                    dirtyQuestions.delete(questionIndex);
                    failedQuestions.delete(questionIndex);
                }
                updateSaveStatus();
            } catch {
                // Lỗi của một version cũ không che lỗi/trạng thái của version mới hơn.
                if (versions.get(questionIndex) === currentChange) {
                    dirtyQuestions.add(questionIndex);
                    failedQuestions.add(questionIndex);
                    updateSaveStatus();
                }
            }
        });
    });

    // Giữ lựa chọn trên trang khi mất mạng; cảnh báo nếu rời đi trước lúc lưu xong.
    // Lưu thủ công/Nộp bài là điều hướng có chủ đích nên không hiện cảnh báo này.
    window.addEventListener('beforeunload', event => {
        if (dirtyQuestions.size > 0 && !submitting) {
            event.preventDefault();
            event.returnValue = '';
        }
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
        if (remaining === 0 && !expired) {
            expired = true;
            // inert khóa chuột/bàn phím, nhưng vẫn giữ đáp án trong dữ liệu form.
            // Server mới quyết định đáp án nào được nhận, không tin đồng hồ browser.
            questions.forEach(question => { question.inert = true; });
            if (!submitting) {
                if (dialog.open) dialog.close();
                form.requestSubmit(form.querySelector('[data-submit-button]'));
            }
        }
    };
    updateClock();
    window.setInterval(updateClock, 1000);
    document.addEventListener('visibilitychange', updateClock);
})();
