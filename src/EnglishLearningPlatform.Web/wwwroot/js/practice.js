document.querySelectorAll('[data-practice-form]').forEach(form => {
    form.classList.add('js-practice-workspace');
    const questions = [...form.querySelectorAll('[data-practice-question]')];
    const links = [...form.querySelectorAll('[data-practice-link]')];
    const progress = document.querySelector('[data-practice-progress]');
    const bar = document.querySelector('[data-practice-bar]');
    const prevBtn = form.querySelector('[data-practice-prev]');
    const nextBtn = form.querySelector('[data-practice-next]');
    const submitBtn = form.querySelector('[data-practice-submit]');
    const confirmDialog = document.querySelector('[data-practice-confirm-dialog]');
    const answeredCountEl = confirmDialog?.querySelector('[data-answered-count]');
    const unansweredCountEl = confirmDialog?.querySelector('[data-unanswered-count]');
    const cancelBtn = confirmDialog?.querySelector('[data-cancel-submit]');
    const confirmBtn = confirmDialog?.querySelector('[data-confirm-submit]');
    const passage = form.querySelector('#practice-passages');

    let currentIndex = 0;

    if (passage) {
        form.querySelectorAll('.practice-back-passage').forEach(link => {
            link.addEventListener('click', () => {
                passage.open = true;
            });
        });
    }

    function setActiveQuestion(index) {
        if (index < 0 || index >= questions.length) return;
        currentIndex = index;
        questions.forEach((q, i) => {
            const isActive = (i === index);
            q.classList.toggle('is-active-question', isActive);
            if (isActive) {
                q.removeAttribute('inert');
            }
        });
        links.forEach((l, i) => {
            l.classList.toggle('is-current', i === index);
            if (i === index) {
                l.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
            }
        });
        if (prevBtn) prevBtn.disabled = (index === 0);
        if (nextBtn) nextBtn.disabled = (index === questions.length - 1);
    }

    function isQuestionAnswered(question) {
        const checkedRadio = question.querySelector('input[type="radio"]:checked');
        if (checkedRadio && checkedRadio.value) return true;
        const text = question.querySelector('[data-practice-text]') || question.querySelector('input[type="text"]');
        if (text && text.value.trim().length > 0) return true;
        const structured = question.querySelector('[data-structured-answer]');
        if (structured && structured.value.trim().length > 0) return true;
        return false;
    }

    function update() {
        let answered = 0;
        questions.forEach((question, index) => {
            const complete = isQuestionAnswered(question);
            if (complete) answered++;
            if (links[index]) {
                links[index].classList.toggle('is-answered', complete);
                links[index].setAttribute('aria-label', `Đến câu ${index + 1}: ${complete ? 'đã trả lời' : 'chưa trả lời'}`);
            }
            question.querySelectorAll('.exam-option-card').forEach(card => {
                const radio = card.querySelector('input[type="radio"]');
                card.classList.toggle('is-selected', Boolean(radio && radio.checked));
            });
        });
        if (progress) {
            progress.textContent = `${answered}/${questions.length} câu đã trả lời`;
        }
        if (bar && questions.length > 0) {
            const pct = Math.round((answered / questions.length) * 100);
            bar.style.width = `${pct}%`;
        }
        return answered;
    }

    // Palette navigation
    links.forEach((link, index) => {
        link.addEventListener('click', (e) => {
            e.preventDefault();
            setActiveQuestion(index);
        });
    });

    // Next / Prev buttons
    prevBtn?.addEventListener('click', () => {
        if (currentIndex > 0) setActiveQuestion(currentIndex - 1);
    });
    nextBtn?.addEventListener('click', () => {
        if (currentIndex < questions.length - 1) setActiveQuestion(currentIndex + 1);
    });

    // Keyboard navigation
    window.addEventListener('keydown', (e) => {
        if (['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName)) return;
        if (e.key === 'ArrowLeft' && currentIndex > 0) {
            e.preventDefault();
            setActiveQuestion(currentIndex - 1);
        } else if (e.key === 'ArrowRight' && currentIndex < questions.length - 1) {
            e.preventDefault();
            setActiveQuestion(currentIndex + 1);
        }
    });

    // Focus / interaction activates question
    questions.forEach((question, index) => {
        question.addEventListener('focusin', () => {
            if (currentIndex !== index) setActiveQuestion(index);
        });
    });

    // Submission and confirmation modal
    let isConfirmed = false;
    form.addEventListener('submit', (e) => {
        if (isConfirmed) return;

        const answered = update();
        const unanswered = questions.length - answered;
        if (unanswered > 0 && confirmDialog && typeof confirmDialog.showModal === 'function') {
            e.preventDefault();
            if (answeredCountEl) answeredCountEl.textContent = answered;
            if (unansweredCountEl) unansweredCountEl.textContent = unanswered;
            confirmDialog.showModal();
            return;
        }

        // Submitting with all questions answered or confirmed
        if (submitBtn) submitBtn.disabled = true;
    });

    cancelBtn?.addEventListener('click', () => {
        confirmDialog?.close();
        questions[currentIndex]?.focus();
    });

    confirmBtn?.addEventListener('click', () => {
        isConfirmed = true;
        confirmDialog?.close();
        if (submitBtn) submitBtn.disabled = true;
        if (typeof form.requestSubmit === 'function') {
            form.requestSubmit();
        } else {
            form.submit();
        }
    });

    // Listeners
    form.addEventListener('change', update);
    form.addEventListener('input', update);
    window.addEventListener('pageshow', update);

    // Initial state
    setActiveQuestion(0);
    update();
});
