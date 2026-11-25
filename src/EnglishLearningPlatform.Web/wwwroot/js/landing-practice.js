(() => {
    const practice = document.querySelector('[data-landing-practice]');
    if (!practice) return;

    const interactive = practice.querySelector('[data-practice-interactive]');
    const fallback = practice.querySelector('[data-practice-fallback]');
    const form = practice.querySelector('[data-practice-form]');
    const questions = Array.from(form.querySelectorAll('[data-practice-question]'));
    const counter = practice.querySelector('[data-practice-counter]');
    const progress = practice.querySelector('[data-practice-progress]');
    const feedback = form.querySelector('[data-practice-feedback-content]');
    const feedbackLabel = form.querySelector('[data-practice-feedback-label]');
    const feedbackExplanation = form.querySelector('[data-practice-feedback-explanation]');
    const keyHint = form.querySelector('#landing-practice-keys');
    const check = form.querySelector('[data-practice-check]');
    const next = form.querySelector('[data-practice-next]');
    const nextLabel = form.querySelector('[data-practice-next-label]');
    const result = practice.querySelector('[data-practice-result]');
    const scoreLabel = result.querySelector('[data-practice-result-score]');
    const reset = result.querySelector('[data-practice-reset]');

    const speech = window.speechSynthesis;
    let voice;
    let playback;
    let current = 0;
    let score = 0;

    function endPlayback() {
        if (!playback) return;
        playback.button.setAttribute('aria-pressed', 'false');
        playback.button.setAttribute('aria-label', 'Nghe câu mẫu');
        playback = undefined;
    }

    function stopAudio() {
        if (!playback) return;
        endPlayback();
        speech.cancel();
    }

    if (speech && 'SpeechSynthesisUtterance' in window) {
        const audioButtons = form.querySelectorAll('[data-practice-audio]');
        const syncVoices = () => {
            voice = speech.getVoices().find(candidate => candidate.localService && /^en(?:-|$)/i.test(candidate.lang));
            for (const button of audioButtons) button.hidden = !voice;
        };

        form.addEventListener('click', (event) => {
            const button = event.target.closest('[data-practice-audio]');
            if (!voice || !button) return;
            if (playback?.button === button) {
                stopAudio();
                return;
            }

            const text = button.closest('figure').querySelector('blockquote').textContent.trim();
            const utterance = new SpeechSynthesisUtterance(text);
            utterance.voice = voice;
            utterance.lang = voice.lang;
            utterance.rate = .9;
            const release = () => {
                if (playback?.utterance === utterance) endPlayback();
            };
            utterance.addEventListener('end', release);
            utterance.addEventListener('error', release);
            stopAudio();
            playback = { button, utterance };
            button.setAttribute('aria-pressed', 'true');
            button.setAttribute('aria-label', 'Dừng phát');
            speech.speak(utterance);
        });

        speech.addEventListener('voiceschanged', syncVoices);
        syncVoices();
    }

    function showQuestion(moveFocus) {
        stopAudio();
        questions.forEach((question, index) => {
            const active = index === current;
            question.hidden = !active;
            question.toggleAttribute('data-practice-enter', active && moveFocus);
            question.querySelector('fieldset').disabled = !active;
        });
        counter.textContent = `Câu ${current + 1}/${questions.length}`;
        progress.value = current + 1;
        feedback.hidden = true;
        keyHint.hidden = false;
        check.hidden = false;
        next.hidden = true;
        if (moveFocus) questions[current].querySelector('legend').focus();
    }

    form.addEventListener('keydown', (event) => {
        if (check.hidden || event.altKey || event.ctrlKey || event.metaKey || !/^[1-9]$/.test(event.key)) return;
        const input = questions[current].querySelectorAll('input[type="radio"]')[Number(event.key) - 1];
        if (!input || input.disabled) return;
        event.preventDefault();
        input.checked = true;
        input.focus();
    });

    form.addEventListener('submit', (event) => {
        event.preventDefault();
        const question = questions[current];
        const selected = question.querySelector('input:checked');
        if (!selected || check.hidden) return;

        const isRight = selected.value === question.dataset.correctIndex;
        if (isRight) score++;

        for (const input of question.querySelectorAll('input')) {
            const option = input.closest('label');
            if (input.value === question.dataset.correctIndex) {
                option.dataset.practiceOutcome = 'correct';
                option.querySelector('.landing-practice-answer-status').hidden = false;
            } else if (input.checked) {
                option.dataset.practiceOutcome = 'incorrect';
            }
        }

        question.querySelector('fieldset').disabled = true;
        feedback.dataset.practiceOutcome = isRight ? 'correct' : 'incorrect';
        feedbackLabel.textContent = isRight ? 'Chính xác!' : 'Chưa đúng';
        feedbackExplanation.textContent = question.dataset.explanation;
        feedback.hidden = false;
        keyHint.hidden = true;
        check.hidden = true;
        nextLabel.textContent = current === questions.length - 1 ? 'Xem kết quả' : 'Câu tiếp theo';
        next.hidden = false;
        next.focus();
    });

    next.addEventListener('click', () => {
        if (current < questions.length - 1) {
            current++;
            showQuestion(true);
            return;
        }

        stopAudio();
        form.hidden = true;
        counter.textContent = 'Kết quả';
        scoreLabel.textContent = `${score}/${questions.length} câu đúng`;
        result.hidden = false;
        scoreLabel.focus();
    });

    reset.addEventListener('click', () => {
        current = 0;
        score = 0;
        form.reset();
        for (const option of form.querySelectorAll('.landing-practice-option[data-practice-outcome]')) {
            delete option.dataset.practiceOutcome;
            option.querySelector('.landing-practice-answer-status').hidden = true;
        }
        result.hidden = true;
        form.hidden = false;
        showQuestion(true);
    });

    interactive.hidden = false;
    fallback.hidden = true;
    showQuestion(false);
})();
