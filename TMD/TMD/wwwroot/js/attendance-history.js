// wwwroot/js/attendance-history.js

// View Details Function
function viewDetails(data) {
    // Work Date
    const workDate = new Date(data.workDate);
    const dateOptions = {
        weekday: 'long',
        year: 'numeric',
        month: 'long',
        day: 'numeric'
    };
    document.getElementById('detailWorkDate').innerHTML =
        `${workDate.toLocaleDateString('vi-VN', dateOptions)}`;

    // Check-in Info
    if (data.checkInTime) {
        const checkInDate = new Date(data.checkInTime);
        document.getElementById('detailCheckInTime').innerHTML = `
            <div style="display: flex; align-items: center; gap: 10px;">
                <strong>${checkInDate.toLocaleDateString('vi-VN')}</strong>
                <span class="time-badge time-in">
                    <i class="fas fa-clock"></i>
                    ${checkInDate.toLocaleTimeString('vi-VN')}
                </span>
            </div>
        `;
    } else {
        document.getElementById('detailCheckInTime').innerHTML =
            '<span style="color: #adb5bd;">Chưa check-in</span>';
    }

    document.getElementById('detailCheckInAddress').textContent =
        data.checkInAddress || 'Không có thông tin';

    // Check-in Photo
    if (data.checkInPhotos) {
        document.getElementById('checkInPhotoGroup').style.display = 'flex';
        document.getElementById('checkInPhotoContainer').innerHTML = `
            <img src="${data.checkInPhotos}" 
                 alt="Check-in"
                 onclick="openImageModal('${data.checkInPhotos}', 'Check-in')">
        `;
    } else {
        document.getElementById('checkInPhotoGroup').style.display = 'none';
    }

    // Check-out Info
    if (data.checkOutTime) {
        const checkOutDate = new Date(data.checkOutTime);
        document.getElementById('detailCheckOutTime').innerHTML = `
            <div style="display: flex; align-items: center; gap: 10px;">
                <strong>${checkOutDate.toLocaleDateString('vi-VN')}</strong>
                <span class="time-badge time-out">
                    <i class="fas fa-clock"></i>
                    ${checkOutDate.toLocaleTimeString('vi-VN')}
                </span>
            </div>
        `;
    } else {
        document.getElementById('detailCheckOutTime').innerHTML =
            '<span class="status-badge badge-warning"><i class="fas fa-clock"></i> Chưa check-out</span>';
    }

    // Total Hours
    if (data.totalHours && data.totalHours > 0) {
        document.getElementById('detailTotalHours').innerHTML = `
            <i class="fas fa-hourglass-half"></i> ${data.totalHours.toFixed(2)} giờ
        `;
    } else {
        document.getElementById('detailTotalHours').innerHTML =
            '<span style="color: #adb5bd;">—</span>';
    }

    document.getElementById('detailCheckOutAddress').textContent =
        data.checkOutAddress || 'Không có thông tin';

    // Check-out Photo
    if (data.checkOutPhotos) {
        document.getElementById('checkOutPhotoGroup').style.display = 'flex';
        document.getElementById('checkOutPhotoContainer').innerHTML = `
            <img src="${data.checkOutPhotos}" 
                 alt="Check-out"
                 onclick="openImageModal('${data.checkOutPhotos}', 'Check-out')">
        `;
    } else {
        document.getElementById('checkOutPhotoGroup').style.display = 'none';
    }

    // Show modal
    const modal = new bootstrap.Modal(document.getElementById('detailModal'));
    modal.show();
}

// Open Image in Modal (Optional - for full screen view)
function openImageModal(imageUrl, title) {
    window.open(imageUrl, '_blank');
}

// Search Functionality
document.addEventListener('DOMContentLoaded', function () {
    const searchInput = document.getElementById('searchInput');

    if (searchInput) {
        searchInput.addEventListener('input', function () {
            const searchTerm = this.value.toLowerCase();
            const tableRows = document.querySelectorAll('.modern-table tbody tr');

            tableRows.forEach(row => {
                const dateCell = row.querySelector('.date-primary');
                if (dateCell) {
                    const dateText = dateCell.textContent.toLowerCase();
                    if (dateText.includes(searchTerm)) {
                        row.style.display = '';
                    } else {
                        row.style.display = 'none';
                    }
                }
            });
        });
    }
});

// Export to Excel Function (Placeholder)
function exportAttendance() {
    alert('Chức năng xuất Excel đang được phát triển!');
    // TODO: Implement Excel export functionality
    // You can use libraries like SheetJS (xlsx) or server-side export
}

// Animation on scroll (Optional)
const observerOptions = {
    threshold: 0.1,
    rootMargin: '0px 0px -50px 0px'
};

const observer = new IntersectionObserver((entries) => {
    entries.forEach(entry => {
        if (entry.isIntersecting) {
            entry.target.style.opacity = '1';
            entry.target.style.transform = 'translateY(0)';
        }
    });
}, observerOptions);

// Observe stat cards for animation
document.addEventListener('DOMContentLoaded', function () {
    const statCards = document.querySelectorAll('.stat-card');
    statCards.forEach((card, index) => {
        card.style.opacity = '0';
        card.style.transform = 'translateY(20px)';
        card.style.transition = `all 0.5s ease ${index * 0.1}s`;
        observer.observe(card);
    });
});

// Print Modal Content (Optional feature)
function printDetails() {
    window.print();
}

// Filter functionality (placeholder for future implementation)
document.addEventListener('DOMContentLoaded', function () {
    const filterBtn = document.querySelector('.btn-filter');

    if (filterBtn) {
        filterBtn.addEventListener('click', function () {
            alert('Chức năng lọc đang được phát triển!');
            // TODO: Implement filter dropdown
            // - Filter by date range
            // - Filter by status (late, on-time)
            // - Filter by completed/incomplete
        });
    }
});

// Keyboard shortcuts
document.addEventListener('keydown', function (e) {
    // Ctrl/Cmd + F: Focus search
    if ((e.ctrlKey || e.metaKey) && e.key === 'f') {
        e.preventDefault();
        const searchInput = document.getElementById('searchInput');
        if (searchInput) {
            searchInput.focus();
        }
    }

    // Escape: Close modal
    if (e.key === 'Escape') {
        const modal = bootstrap.Modal.getInstance(document.getElementById('detailModal'));
        if (modal) {
            modal.hide();
        }
    }
});

// Tooltip for truncated text (optional)
document.addEventListener('DOMContentLoaded', function () {
    const addressTexts = document.querySelectorAll('.address-text');
    addressTexts.forEach(text => {
        if (text.scrollWidth > text.clientWidth) {
            text.title = text.textContent;
        }
    });
});