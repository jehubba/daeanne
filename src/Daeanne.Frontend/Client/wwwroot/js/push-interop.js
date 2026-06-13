// Daeanne PWA — Push, Badge, and Web Share interop helpers
// Called from Blazor via IJSRuntime.
window.daeannePush = {

    // Subscribe to Web Push notifications and POST the subscription to /api/subscribe.
    // vapidPublicKey: VAPID public key (Base64url string) from app settings.
    // Returns true on success, false if push is unsupported or subscription fails.
    subscribeToPush: async function (vapidPublicKey) {
        if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
            return false;
        }
        try {
            const registration = await navigator.serviceWorker.ready;
            const existing = await registration.pushManager.getSubscription();
            if (existing) {
                // Already subscribed — re-send to ensure server has it
                await window.daeannePush._postSubscription(existing);
                return true;
            }
            const subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: window.daeannePush._urlBase64ToUint8Array(vapidPublicKey)
            });
            await window.daeannePush._postSubscription(subscription);
            return true;
        } catch (err) {
            console.warn('Push subscription failed:', err);
            return false;
        }
    },

    // Post the PushSubscription to the /api/subscribe endpoint.
    _postSubscription: async function (subscription) {
        await fetch('/api/subscribe', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(subscription.toJSON())
        });
    },

    // Unsubscribe from push notifications.
    unsubscribeFromPush: async function () {
        if (!('serviceWorker' in navigator) || !('PushManager' in window)) return;
        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();
            if (subscription) {
                await subscription.unsubscribe();
            }
        } catch (err) {
            console.warn('Push unsubscribe failed:', err);
        }
    },

    // Set the app badge to the given count (Badge API).
    // Silently skips if navigator.setAppBadge is unavailable.
    setBadge: async function (count) {
        if ('setAppBadge' in navigator) {
            try {
                await navigator.setAppBadge(count);
            } catch { /* not supported */ }
        }
    },

    // Clear the app badge (Badge API).
    clearBadge: async function () {
        if ('clearAppBadge' in navigator) {
            try {
                await navigator.clearAppBadge();
            } catch { /* not supported */ }
        }
    },

    // Share a task via the Web Share API.
    // Returns true on success, false if unavailable or the user cancelled.
    shareTask: async function (title, text, url) {
        if (!navigator.share) return false;
        try {
            await navigator.share({ title, text, url });
            return true;
        } catch (err) {
            if (err.name !== 'AbortError') {
                console.warn('Web Share failed:', err);
            }
            return false;
        }
    },

    // Check whether Web Share is available in this browser.
    isShareSupported: function () {
        return !!navigator.share;
    },

    // Convert a Base64url-encoded VAPID public key to the Uint8Array that
    // PushManager.subscribe() expects.
    _urlBase64ToUint8Array: function (base64String) {
        const padding = '='.repeat((4 - base64String.length % 4) % 4);
        const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
        const rawData = window.atob(base64);
        const outputArray = new Uint8Array(rawData.length);
        for (let i = 0; i < rawData.length; ++i) {
            outputArray[i] = rawData.charCodeAt(i);
        }
        return outputArray;
    }
};
