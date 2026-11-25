(() => {
    const flip = document.querySelector('[data-flashcard-flip]');
    if (flip) flip.addEventListener('click', () => {
        const expanded = flip.getAttribute('aria-expanded') !== 'true';
        flip.setAttribute('aria-expanded', String(expanded));
        flip.classList.toggle('is-flipped', expanded);
        const front = flip.querySelector('[data-flashcard-front]');
        if (front) front.setAttribute('aria-hidden', String(expanded));
        const back = document.getElementById('card-back');
        if (back) back.setAttribute('aria-hidden', String(!expanded));
        document.querySelectorAll('[data-flashcard-answer]').forEach(el => { el.hidden = !expanded; });
    });
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
