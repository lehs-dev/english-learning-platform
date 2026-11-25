const calendar = document.getElementById("calendar-widget");
if (calendar) {
    const days = [...calendar.querySelectorAll("[data-calendar-day]")];
    const panels = [...calendar.querySelectorAll("[data-calendar-panel]")];

    calendar.addEventListener("click", event => {
        const selected = event.target.closest("[data-calendar-day]");
        if (!selected) return;

        for (const day of days) {
            day.classList.toggle("active", day === selected);
            day.setAttribute("aria-pressed", String(day === selected));
        }
        for (const panel of panels) {
            panel.hidden = panel.dataset.calendarPanel !== selected.dataset.calendarDay;
        }
    });
}
