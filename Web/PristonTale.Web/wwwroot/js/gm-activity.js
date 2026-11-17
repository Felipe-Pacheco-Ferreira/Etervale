const activitySections = ['gm-live-stats', 'gm-global-chat', 'gm-whispers'];

async function refreshActivity() {
    try {
        if (document.hidden) return;
        const response = await fetch(location.href, { cache: 'no-store' });
        if (!response.ok || response.redirected) return;
        const next = new DOMParser().parseFromString(await response.text(), 'text/html');
        for (const id of activitySections) {
            const current = document.getElementById(id);
            const updated = next.getElementById(id);
            if (current && updated && current.innerHTML !== updated.innerHTML) {
                current.replaceWith(updated);
            }
        }
    } finally {
        window.setTimeout(() => refreshActivity().catch(console.warn), 1000);
    }
}

window.setTimeout(() => refreshActivity().catch(console.warn), 1000);
