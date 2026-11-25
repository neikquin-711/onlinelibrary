(() => {
  const triggers = [...document.querySelectorAll(".hot-slide-art, .home-book-cover")];
  if (!triggers.length) return;

  const ownerFor = trigger => trigger.closest(".home-book-card") || trigger;
  const open = trigger => {
    ownerFor(trigger).classList.add("is-details-open");
    trigger.setAttribute("aria-expanded", "true");
  };
  const close = trigger => {
    ownerFor(trigger).classList.remove("is-details-open");
    trigger.setAttribute("aria-expanded", "false");
  };

  for (const trigger of triggers) {
    const owner = ownerFor(trigger);
    let holdTimer;
    let holdTriggered = false;
    let suppressNextClick = false;

    const hoverTarget = trigger.matches(".home-book-cover") ? owner : trigger;
    hoverTarget.addEventListener("pointerenter", event => {
      if (event.pointerType === "mouse") open(trigger);
    });
    hoverTarget.addEventListener("pointerleave", event => {
      if (event.pointerType === "mouse") close(trigger);
    });
    trigger.addEventListener("focus", () => open(trigger));
    trigger.addEventListener("blur", () => close(trigger));

    trigger.addEventListener("pointerdown", event => {
      if (event.pointerType !== "touch" && event.pointerType !== "pen") return;
      holdTriggered = false;
      if (owner.classList.contains("is-details-open")) {
        close(trigger);
        suppressNextClick = true;
        return;
      }
      holdTimer = window.setTimeout(() => {
        holdTriggered = true;
        suppressNextClick = true;
        open(trigger);
      }, 520);
    });
    const cancelHold = () => window.clearTimeout(holdTimer);
    trigger.addEventListener("pointerup", cancelHold);
    trigger.addEventListener("pointercancel", cancelHold);
    trigger.addEventListener("click", event => {
      if (!suppressNextClick && !holdTriggered) return;
      event.preventDefault();
      event.stopPropagation();
      suppressNextClick = false;
      holdTriggered = false;
    });
  }

  document.addEventListener("pointerdown", event => {
    if (event.pointerType !== "touch" && event.pointerType !== "pen") return;
    for (const trigger of triggers) {
      if (!ownerFor(trigger).contains(event.target)) close(trigger);
    }
  });
})();
