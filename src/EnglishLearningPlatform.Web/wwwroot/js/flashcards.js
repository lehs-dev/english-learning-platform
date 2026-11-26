(() => {
    const flip = document.querySelector('[data-flashcard-flip]');
    if (flip) {
        const toggleFlip = () => {
            const expanded = flip.getAttribute('aria-expanded') !== 'true';
            flip.setAttribute('aria-expanded', String(expanded));
            flip.classList.toggle('is-flipped', expanded);
            const front = flip.querySelector('[data-flashcard-front]');
            if (front) front.setAttribute('aria-hidden', String(expanded));
            const back = document.getElementById('card-back');
            if (back) back.setAttribute('aria-hidden', String(!expanded));
            document.querySelectorAll('[data-flashcard-answer]').forEach(el => { el.hidden = !expanded; });
        };
        flip.addEventListener('click', toggleFlip);

        document.addEventListener('keydown', event => {
            const target = event.target;
            if (target instanceof HTMLElement && target.matches('input, textarea, select, audio, video, a')) return;
            if (event.code === 'Space' && (!target || target === document.body || target === flip || !target.matches('button'))) {
                event.preventDefault();
                toggleFlip();
                return;
            }
            if (flip.getAttribute('aria-expanded') === 'true' && ['1', '2', '3'].includes(event.key)) {
                const ratingBtn = document.querySelector(`[data-flashcard-rating="${event.key}"]`);
                if (ratingBtn instanceof HTMLButtonElement) {
                    event.preventDefault();
                    ratingBtn.click();
                }
            }
        });
    }
    const course = document.querySelector('[data-flashcard-course]');
    const lesson = document.querySelector('[data-flashcard-lesson]');
    if (course && lesson) {
        const filter = () => {
            for (const option of lesson.options) {
                const unavailable = Boolean(option.dataset.course && option.dataset.course !== course.value);
                option.hidden = unavailable;
                option.disabled = unavailable;
                if (unavailable && option.selected) lesson.value = '';
            }
        };
        course.addEventListener('change', filter);
        filter();
    }
})();
