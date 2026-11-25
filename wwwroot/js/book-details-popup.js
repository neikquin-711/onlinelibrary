(() => {
  const bookLinks = [...document.querySelectorAll(".hot-slide-art")];
  if (!bookLinks.length) return;

  const open = link => {
    link.classList.add("is-details-open");
    link.setAttribute("aria-expanded", "true");
  };
  const close = link => {
    link.classList.remove("is-details-open");
    link.setAttribute("aria-expanded", "false");
  };

  for (const link of bookLinks) {
    let holdTimer;
    let holdTriggered = false;
    let suppressNextClick = false;

    link.addEventListener("pointerenter", event => {
      if (event.pointerType === "mouse") open(link);
    });
    link.addEventListener("pointerleave", event => {
      if (event.pointerType === "mouse") close(link);
    });
    link.addEventListener("focus", () => open(link));
    link.addEventListener("blur", () => close(link));

    link.addEventListener("pointerdown", event => {
      if (event.pointerType !== "touch" && event.pointerType !== "pen") return;
      holdTriggered = false;
      if (link.classList.contains("is-details-open")) {
        close(link);
        suppressNextClick = true;
        return;
      }
      holdTimer = window.setTimeout(() => {
        holdTriggered = true;
        suppressNextClick = true;
        open(link);
      }, 520);
    });
    const cancelHold = () => window.clearTimeout(holdTimer);
    link.addEventListener("pointerup", cancelHold);
    link.addEventListener("pointercancel", cancelHold);
    link.addEventListener("click", event => {
      if (!suppressNextClick && !holdTriggered) return;
      event.preventDefault();
      event.stopPropagation();
      suppressNextClick = false;
      holdTriggered = false;
    });
  }

  document.addEventListener("pointerdown", event => {
    if (event.pointerType !== "touch" && event.pointerType !== "pen") return;
    for (const link of bookLinks) {
      if (!link.contains(event.target)) close(link);
    }
  });
})();
