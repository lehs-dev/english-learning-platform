// <details> mở được bằng chuột/bàn phím ngay cả khi JavaScript tắt.
document.querySelectorAll("[data-account-menu]").forEach((menu) => {
    document.addEventListener("click", (event) => {
        if (!menu.contains(event.target)) menu.open = false;
    });
    menu.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            menu.open = false;
            menu.querySelector("summary").focus();
        }
    });
});

const navToggle = document.querySelector("[data-nav-toggle]");
const navigation = document.getElementById("main-nav");
if (navToggle && navigation) {
    navToggle.addEventListener("click", () => {
        const isOpen = navigation.classList.toggle("is-open");
        navToggle.setAttribute("aria-expanded", String(isOpen));
        navToggle.setAttribute("aria-label", isOpen ? "Đóng thanh điều hướng" : "Mở thanh điều hướng");
    });
    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && navigation.classList.contains("is-open")) {
            navigation.classList.remove("is-open");
            navToggle.setAttribute("aria-expanded", "false");
            navToggle.setAttribute("aria-label", "Mở thanh điều hướng");
            navToggle.focus();
        }
    });
}

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
