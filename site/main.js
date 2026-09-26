// Two small jobs: point the download buttons at the files in the newest release, and put the
// Mac button first for Mac visitors. Without JavaScript, the buttons open the releases page.

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
