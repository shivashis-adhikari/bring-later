// Point the download buttons at the files in the newest release, put the Mac button first for Mac
// visitors, and play the demo silently. Without JavaScript, the buttons open the releases page.

const nav = document.querySelector("[data-nav]");
const onScroll = () => nav.classList.toggle("is-scrolled", window.scrollY > 8);
window.addEventListener("scroll", onScroll, { passive: true });
onScroll();

const isMac = /mac/i.test(navigator.userAgentData?.platform ?? navigator.platform ?? "");
const primary = document.querySelector('[data-download="primary"]');
const secondary = document.querySelector('[data-download="secondary"]');
if (isMac) {
  primary.textContent = "Download for Mac";
  secondary.textContent = "Download for Windows";
}

// The demo loops without sound; its controls turn the sound on. Reduced motion and Data Saver keep it
// on the poster. Frame 0 is the poster, so each loop starts just after it.
const demo = document.querySelector("[data-demo]");
if (!matchMedia("(prefers-reduced-motion: reduce)").matches && !navigator.connection?.saveData) {
  demo.muted = true;
  demo.addEventListener("ended", () => {
    if (!demo.muted) return;
    demo.currentTime = 0.05;
    demo.play().catch(() => {});
  });
  demo.play().catch(() => {});
}

const assetFor = {
  "win-x64": (name) => /win-x64\.exe$/.test(name),
  "win-arm64": (name) => /win-arm64\.exe$/.test(name),
  mac: (name) => /mac\.zip$/.test(name),
};

fetch("https://api.github.com/repos/shivashis-adhikari/bring-later/releases?per_page=10", {
  headers: { Accept: "application/vnd.github+json" },
})
  .then((response) => (response.ok ? response.json() : []))
  .then((releases) => {
    const release = releases.find((r) => !r.draft && r.assets.length > 0);
    if (!release) return;
    const url = (key) => release.assets.find((asset) => assetFor[key](asset.name))?.browser_download_url;

    document.querySelectorAll("[data-asset]").forEach((link) => {
      const href = url(link.dataset.asset);
      if (href) link.href = href;
    });
    const windows = url("win-x64");
    const mac = url("mac");
    if (windows && mac) {
      primary.href = isMac ? mac : windows;
      secondary.href = isMac ? windows : mac;
    }
  })
  .catch(() => {
    // Offline or rate-limited: the buttons keep pointing at the releases page.
  });
