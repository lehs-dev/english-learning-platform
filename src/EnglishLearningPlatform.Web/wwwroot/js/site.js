// <details> mở được bằng chuột/bàn phím ngay cả khi JavaScript tắt.
document.querySelectorAll("[data-account-menu]").forEach((menu) => {
    document.addEventListener("click", (event) => {
        if (!menu.contains(event.target)) menu.open = false;
    });
    menu.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && menu.open) {
            event.stopPropagation();
            menu.open = false;
            menu.querySelector("summary").focus();
        }
    });
});

// Resolve the controlled element for every role, including the student header.
document.querySelectorAll("[data-nav-toggle], [data-sidebar-toggle]").forEach((toggle) => {
    const navigation = document.getElementById(toggle.getAttribute("aria-controls"));
    if (!navigation) return;

    const isSidebar = navigation.matches(".studio-sidebar, .admin-sidebar");
    const mobile = window.matchMedia(document.body.classList.contains("question-bank-focus-page")
        ? "(min-width: 0px)"
        : "(max-width: 1024px)");
    const backdrop = [...document.querySelectorAll("[data-sidebar-backdrop]")]
        .find((element) => element.getAttribute("aria-controls") === navigation.id);
    const closedLabel = toggle.getAttribute("aria-label") || "Mở thanh điều hướng";
    // Browsers may blur a control as soon as its media query hides it, before
    // the change listener runs. Remember focus so it can move to a visible control.
    let lastFocused = document.activeElement;
    document.addEventListener("focusin", (event) => {
        if (event.target !== document.body) lastFocused = event.target;
    });
    const focusableElements = () => [...navigation.querySelectorAll(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), summary, [tabindex="0"]'
    )].filter((element) => !element.closest("[inert]") && element.checkVisibility());

    function setOpen(open, restoreFocus = false) {
        open = open && mobile.matches;
        navigation.classList.toggle("is-open", open);
        toggle.setAttribute("aria-expanded", String(open));
        toggle.setAttribute("aria-label", open ? closedLabel.replace(/^Mở/, "Đóng") : closedLabel);

        if (isSidebar) {
            navigation.inert = mobile.matches && !open;
            if (mobile.matches && !open) navigation.setAttribute("aria-hidden", "true");
            else navigation.removeAttribute("aria-hidden");
            if (open) {
                navigation.setAttribute("role", "dialog");
                navigation.setAttribute("aria-modal", "true");
            } else {
                navigation.removeAttribute("role");
                navigation.removeAttribute("aria-modal");
            }
            if (backdrop) backdrop.hidden = !open;
            document.body.classList.toggle("sidebar-open", open);
            if (open) focusableElements()[0]?.focus();
        }

        if (restoreFocus) toggle.focus();
    }

    toggle.addEventListener("click", () => setOpen(!navigation.classList.contains("is-open")));
    navigation.querySelector("[data-sidebar-close]")?.addEventListener("click", () => setOpen(false, true));
    backdrop?.addEventListener("click", () => setOpen(false, true));
    navigation.addEventListener("click", (event) => {
        if (mobile.matches && event.target.closest("a[href]")) setOpen(false, true);
    });
    document.addEventListener("click", (event) => {
        if (!isSidebar && !navigation.contains(event.target) && !toggle.contains(event.target)) setOpen(false);
    });
    document.addEventListener("keydown", (event) => {
        if (!navigation.classList.contains("is-open")) return;
        if (event.key === "Escape") {
            event.preventDefault();
            setOpen(false, true);
        } else if (isSidebar && event.key === "Tab") {
            const elements = focusableElements();
            const first = elements[0];
            const last = elements[elements.length - 1];
            if (event.shiftKey && (document.activeElement === first || !navigation.contains(document.activeElement))) {
                event.preventDefault();
                last?.focus();
            } else if (!event.shiftKey && (document.activeElement === last || !navigation.contains(document.activeElement))) {
                event.preventDefault();
                first?.focus();
            }
        }
    });
    mobile.addEventListener("change", () => {
        const focused = document.activeElement === document.body ? lastFocused : document.activeElement;
        const focusWillBeHidden = mobile.matches
            ? navigation.contains(focused)
            : focused === toggle || (navigation.contains(focused) && focused.matches("[data-sidebar-close]"));
        setOpen(false, focusWillBeHidden && mobile.matches);
        if (focusWillBeHidden && !mobile.matches) focusableElements()[0]?.focus();
    });
    setOpen(false);
});

document.querySelectorAll("[data-password-toggle]").forEach((button) => {
    button.addEventListener("click", () => {
        const input = document.getElementById(button.dataset.passwordToggle);
        if (!input) return;
        const showPassword = input.type === "password";
        input.type = showPassword ? "text" : "password";
        button.setAttribute("aria-pressed", String(showPassword));
        button.setAttribute("aria-label", showPassword ? "Ẩn mật khẩu" : "Hiện mật khẩu");
    });
});

document.querySelectorAll("[data-local-date]").forEach((element) => {
    const date = new Date(element.dateTime);
    if (!Number.isNaN(date.getTime())) element.textContent = date.toLocaleDateString("vi-VN");
});
