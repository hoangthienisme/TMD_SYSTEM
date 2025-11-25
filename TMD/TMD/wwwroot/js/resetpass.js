
        // Toggle password visibility
        function togglePassword(inputId, icon) {
            const input = document.getElementById(inputId);
            const i = icon.querySelector('i');
            
            if (input.type === 'password') {
                input.type = 'text';
                i.classList.remove('fa-eye');
                i.classList.add('fa-eye-slash');
            } else {
                input.type = 'password';
                i.classList.remove('fa-eye-slash');
                i.classList.add('fa-eye');
            }
        }

        // Check password strength
        document.getElementById('newPassword').addEventListener('input', function() {
            const password = this.value;
            const strengthBar = document.getElementById('strengthBar');
            const strengthText = document.getElementById('strengthText');
            
            let strength = 0;
            
            // Check length
            const reqLength = document.getElementById('req-length');
            if (password.length >= 6) {
                strength += 25;
                reqLength.classList.add('valid');
                reqLength.querySelector('i').classList.remove('fa-circle');
                reqLength.querySelector('i').classList.add('fa-check-circle');
            } else {
                reqLength.classList.remove('valid');
                reqLength.querySelector('i').classList.remove('fa-check-circle');
                reqLength.querySelector('i').classList.add('fa-circle');
            }
            
            // Check number
            const reqNumber = document.getElementById('req-number');
            if (/\d/.test(password)) {
                strength += 25;
                reqNumber.classList.add('valid');
                reqNumber.querySelector('i').classList.remove('fa-circle');
                reqNumber.querySelector('i').classList.add('fa-check-circle');
            } else {
                reqNumber.classList.remove('valid');
                reqNumber.querySelector('i').classList.remove('fa-check-circle');
                reqNumber.querySelector('i').classList.add('fa-circle');
            }
            
            // Check letter
            const reqLetter = document.getElementById('req-letter');
            if (/[a-zA-Z]/.test(password)) {
                strength += 25;
                reqLetter.classList.add('valid');
                reqLetter.querySelector('i').classList.remove('fa-circle');
                reqLetter.querySelector('i').classList.add('fa-check-circle');
            } else {
                reqLetter.classList.remove('valid');
                reqLetter.querySelector('i').classList.remove('fa-check-circle');
                reqLetter.querySelector('i').classList.add('fa-circle');
            }
            
            // Check special char or uppercase
            if (/[A-Z]/.test(password) || /[!@#$%^&*(),.?":{}|<>]/.test(password)) {
                strength += 25;
            }
            
            // Update strength bar
            strengthBar.style.width = strength + '%';
            
            if (strength <= 25) {
                strengthBar.style.background = '#ff6b6b';
                strengthText.textContent = 'Yếu';
                strengthText.style.color = '#ff6b6b';
            } else if (strength <= 50) {
                strengthBar.style.background = '#ffa726';
                strengthText.textContent = 'Trung bình';
                strengthText.style.color = '#ffa726';
            } else if (strength <= 75) {
                strengthBar.style.background = '#66bb6a';
                strengthText.textContent = 'Tốt';
                strengthText.style.color = '#66bb6a';
            } else {
                strengthBar.style.background = '#28a745';
                strengthText.textContent = 'Mạnh';
                strengthText.style.color = '#28a745';
            }
        });

        // Handle form submission
        document.getElementById('resetPasswordForm').addEventListener('submit', async function(e) {
            e.preventDefault();
            
            const email = document.getElementById('email').value;
            const otpCode = document.getElementById('otpCode').value;
            const newPassword = document.getElementById('newPassword').value;
            const confirmPassword = document.getElementById('confirmPassword').value;
            
            // Validate
            if (newPassword.length < 6) {
                Swal.fire({
                    icon: 'warning',
                    title: 'Chú ý!',
                    text: 'Mật khẩu phải có ít nhất 6 ký tự',
                    confirmButtonColor: '#667eea'
                });
                return;
            }
            
            if (newPassword !== confirmPassword) {
                Swal.fire({
                    icon: 'error',
                    title: 'Lỗi!',
                    text: 'Mật khẩu xác nhận không khớp',
                    confirmButtonColor: '#667eea'
                });
                return;
            }

            const btn = document.getElementById('btnReset');
            const originalText = btn.innerHTML;
            btn.disabled = true;
            btn.innerHTML = '<i class="fas fa-spinner fa-spin me-2"></i>Đang xử lý...';

            try {
                const response = await fetch('/Account/ResetPasswordJson', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({
                        email: email,
                        otpCode: otpCode,
                        newPassword: newPassword,
                        confirmPassword: confirmPassword
                    })
                });

                const result = await response.json();

                if (result.success) {
                    await Swal.fire({
                        icon: 'success',
                        title: 'Thành công!',
                        text: result.message,
                        confirmButtonColor: '#667eea',
                        showConfirmButton: true
                    });
                    
                    window.location.href = '/Account/Login';
                } else {
                    Swal.fire({
                        icon: 'error',
                        title: 'Lỗi!',
                        text: result.message,
                        confirmButtonColor: '#667eea'
                    });
                    
                    btn.disabled = false;
                    btn.innerHTML = originalText;
                }
            } catch (error) {
                Swal.fire({
                    icon: 'error',
                    title: 'Lỗi!',
                    text: 'Có lỗi xảy ra. Vui lòng thử lại',
                    confirmButtonColor: '#667eea'
                });
                
                btn.disabled = false;
                btn.innerHTML = originalText;
            }
        });
