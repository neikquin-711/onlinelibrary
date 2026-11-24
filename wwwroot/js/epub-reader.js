(() => {
  const container = document.getElementById("epub-reader");
  if (!container) return;

  const status = document.getElementById("epub-status");
  const error = document.getElementById("epub-error");
  if (typeof ePub !== "function") {
    status.hidden = true;
    error.hidden = false;
    return;
  }

  const book = ePub(container.dataset.bookUrl);
  const rendition = book.renderTo(container, {
    width: "100%",
    height: "100%",
    flow: "paginated",
    spread: "none"
  });

  rendition.hooks.content.register(contents => {
    contents.addStylesheetRules({
      body: { "font-family": "Georgia, serif", "line-height": "1.8", color: "#343830", padding: "1rem" },
      p: { "margin-bottom": "1em" },
      img: { "max-width": "100%", height: "auto" }
    });
  });

  rendition.display().then(() => { status.hidden = true; }).catch(() => {
    status.hidden = true;
    error.hidden = false;
  });

  document.getElementById("epub-prev")?.addEventListener("click", () => rendition.prev());
  document.getElementById("epub-next")?.addEventListener("click", () => rendition.next());
  document.addEventListener("keydown", event => {
    if (event.altKey || event.ctrlKey || event.metaKey || /INPUT|TEXTAREA|SELECT/.test(event.target.tagName)) return;
    if (event.key === "ArrowLeft") rendition.prev();
    if (event.key === "ArrowRight") rendition.next();
  });
  window.addEventListener("beforeunload", () => book.destroy());
})();
