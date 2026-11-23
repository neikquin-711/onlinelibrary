let toastTimer;
function showToast(message){const toast=document.getElementById('toast');toast.textContent=message;toast.classList.add('show');clearTimeout(toastTimer);toastTimer=setTimeout(()=>toast.classList.remove('show'),2400)}
function toggleFavorite(button){button.classList.toggle('saved');button.textContent=button.classList.contains('saved')?'♥':'♡';showToast(button.classList.contains('saved')?'Đã lưu vào danh sách yêu thích':'Đã xóa khỏi danh sách yêu thích')}
function changeFont(amount){const content=document.getElementById('readerContent');const current=parseFloat(getComputedStyle(content).fontSize);content.style.fontSize=Math.max(13,Math.min(24,current+amount))+'px'}
