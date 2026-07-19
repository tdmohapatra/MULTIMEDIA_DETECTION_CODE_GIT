// Lightweight pjax-style SPA router. No framework, no server changes.
// Fetches the target page's full HTML (same URL a normal navigation would hit),
// swaps just #spa-content, and re-executes that page's own <script> block
// (the contents of @section Scripts, rendered server-side into #spa-page-scripts).
// The shell (_Layout.cshtml: nav, footer, jQuery/Bootstrap/site.js) never reloads.
(function () {
    const content = document.getElementById('spa-content');
    const scriptsHost = document.getElementById('spa-page-scripts');
    if (!content || !scriptsHost) return;

    let navigating = false;

    function runPageScripts(sourceHost) {
        const scripts = Array.from(sourceHost.querySelectorAll('script'));
        scriptsHost.innerHTML = '';
        scripts.forEach(old => {
            const fresh = document.createElement('script');
            for (const attr of old.attributes) fresh.setAttribute(attr.name, attr.value);
            fresh.textContent = old.textContent;
            scriptsHost.appendChild(fresh);
        });
    }

    function teardownCurrentPage() {
        if (typeof window.__spaTeardown === 'function') {
            try {
                window.__spaTeardown();
            } catch (err) {
                console.error('SPA page teardown failed:', err);
            }
        }
        window.__spaTeardown = null;
    }

    async function loadPage(url, push) {
        if (navigating) return;
        navigating = true;
        try {
            const res = await fetch(url, { credentials: 'same-origin' });
            if (!res.ok) {
                window.location.href = url;
                return;
            }
            const html = await res.text();
            const doc = new DOMParser().parseFromString(html, 'text/html');
            const newContent = doc.getElementById('spa-content');
            if (!newContent) {
                // Target isn't rendered through our layout (e.g. an error page) — do a real navigation.
                window.location.href = url;
                return;
            }

            teardownCurrentPage();

            content.innerHTML = newContent.innerHTML;
            document.title = doc.title;
            if (push) history.pushState({ spa: true }, '', url);
            window.scrollTo(0, 0);

            const newScriptsHost = doc.getElementById('spa-page-scripts');
            if (newScriptsHost) runPageScripts(newScriptsHost);

            document.dispatchEvent(new CustomEvent('spa:navigated', { detail: { url } }));
        } catch (err) {
            console.error('SPA navigation failed, falling back to full page load:', err);
            window.location.href = url;
        } finally {
            navigating = false;
        }
    }

    document.addEventListener('click', (e) => {
        if (e.defaultPrevented || e.button !== 0) return;
        if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;

        const a = e.target.closest('a[href]');
        if (!a) return;
        if (a.target && a.target !== '_self') return;
        if (a.hasAttribute('download')) return;
        if (a.dataset.spaIgnore !== undefined) return;

        let url;
        try {
            url = new URL(a.href, window.location.href);
        } catch {
            return;
        }
        if (url.origin !== window.location.origin) return;
        if (url.pathname === window.location.pathname && url.search === window.location.search && url.hash) {
            return; // same-page anchor jump, let the browser handle it
        }

        e.preventDefault();
        loadPage(url.href, true);
    });

    window.addEventListener('popstate', () => {
        loadPage(window.location.href, false);
    });

    window.addEventListener('beforeunload', teardownCurrentPage);
})();
