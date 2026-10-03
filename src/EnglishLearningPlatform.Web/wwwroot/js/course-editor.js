const priceType = document.querySelector('[data-price-type]');
const price = document.querySelector('[data-price]');
function updatePrice() {
    if (!priceType || !price) return;
    const free = priceType.value.toLowerCase() === 'false';
    price.readOnly = free;
    if (free) price.value = '0';
}
priceType?.addEventListener('change', updatePrice);
updatePrice();
const resourceType = document.querySelector('[data-resource-type]');
function updateResource() {
    if (!resourceType) return;
    const text = resourceType.value === '1';
    document.querySelector('[data-text-fields]').hidden = !text;
    document.querySelector('[data-url-fields]').hidden = text;
}
resourceType?.addEventListener('change', updateResource);
updateResource();
