(() => {
  const track = document.getElementById("hot-book-track");
  if (!track) return;

  const slides = [...track.querySelectorAll(".hot-slide")];
  const dots = [...document.querySelectorAll("[data-hot-dot]")];
  const counter = document.querySelector("[data-hot-count]");
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
  let current = 0;
  let hovering = false;
  let focused = false;

  const goTo = index => {
    current = (index + slides.length) % slides.length;
    const slide = slides[current];
    track.scrollTo({
      left: slide.offsetLeft - track.offsetLeft,
      behavior: reducedMotion.matches ? "auto" : "smooth"
    });
    updateControls();
  };

  const updateControls = () => {
    const index = Math.max(0, Math.min(slides.length - 1, Math.round(track.scrollLeft / track.clientWidth)));
    current = index;
    dots.forEach((dot, dotIndex) => {
      dot.classList.toggle("active", dotIndex === index);
      if (dotIndex === index) dot.setAttribute("aria-current", "true");
      else dot.removeAttribute("aria-current");
    });
    if (counter) counter.textContent = `${String(index + 1).padStart(2, "0")} / ${String(slides.length).padStart(2, "0")}`;
  };

  document.querySelector("[data-hot-prev]")?.addEventListener("click", () => goTo(current - 1));
  document.querySelector("[data-hot-next]")?.addEventListener("click", () => goTo(current + 1));
  dots.forEach(dot => dot.addEventListener("click", () => goTo(Number(dot.dataset.hotDot))));
  track.addEventListener("scroll", () => window.requestAnimationFrame(updateControls), { passive: true });
  track.addEventListener("keydown", event => {
    if (event.key === "ArrowLeft") { event.preventDefault(); goTo(current - 1); }
    if (event.key === "ArrowRight") { event.preventDefault(); goTo(current + 1); }
  });
  track.addEventListener("mouseenter", () => { hovering = true; });
  track.addEventListener("mouseleave", () => { hovering = false; });
  track.addEventListener("focusin", () => { focused = true; });
  track.addEventListener("focusout", event => {
    if (!track.contains(event.relatedTarget)) focused = false;
  });
  window.addEventListener("resize", updateControls);

  window.setInterval(() => {
    if (!hovering && !focused && !document.hidden && !reducedMotion.matches) goTo(current + 1);
  }, 6500);
})();
