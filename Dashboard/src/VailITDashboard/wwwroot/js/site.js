// VailIT Dashboard - small helpers. No libraries needed.

// Reload the overview every N seconds (set by data-refresh on <body>), but not while someone is typing.
(function () {
    var seconds = parseInt(document.body.getAttribute('data-refresh') || '0', 10);
    if (!seconds) return;
    setInterval(function () {
        var a = document.activeElement;
        if (a && (a.tagName === 'INPUT' || a.tagName === 'SELECT' || a.tagName === 'TEXTAREA')) return;
        window.location.reload();
    }, seconds * 1000);
})();

// Buttons with data-confirm="..." ask before submitting their form.
document.addEventListener('submit', function (e) {
    var btn = e.submitter;
    if (btn && btn.dataset.confirm && !window.confirm(btn.dataset.confirm)) e.preventDefault();
});

// Copy buttons: <button data-copy="#id">
document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-copy]');
    if (!btn) return;
    var el = document.querySelector(btn.dataset.copy);
    if (!el) return;
    navigator.clipboard.writeText(el.textContent.trim()).then(function () {
        var old = btn.textContent; btn.textContent = 'Copied'; setTimeout(function () { btn.textContent = old; }, 1500);
    });
});
