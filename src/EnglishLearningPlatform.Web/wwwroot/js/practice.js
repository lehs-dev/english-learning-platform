document.querySelectorAll('[data-practice-form]').forEach(form => {
    const questions = [...form.querySelectorAll('[data-practice-question]')];
    const progress = form.querySelector('[data-practice-progress]');
    const links = [...form.querySelectorAll('[data-practice-link]')];
    function update() {
        let answered = 0;
        questions.forEach((question, index) => {
            const checked = question.querySelector('input[type="radio"]:checked');
            const complete = Boolean(checked && checked.value);
            if (complete) answered++;
            links[index]?.classList.toggle('is-answered', complete);
            links[index]?.setAttribute('aria-label', `Đến câu ${index + 1}: ${complete ? 'đã trả lời' : 'chưa trả lời'}`);
            question.querySelectorAll('.exam-option-card').forEach(card => {
                const radio = card.querySelector('input[type="radio"]');
                card.classList.toggle('is-selected', Boolean(radio && radio.checked));
            });
        });
        if (progress) progress.textContent = `${answered}/${questions.length} câu đã trả lời`;
    }
    form.addEventListener('change', update);
    window.addEventListener('pageshow', update);
    update();
});
