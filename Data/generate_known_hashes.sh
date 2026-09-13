#!/bin/bash
# Chạy script này trên máy Ubuntu để tự tính hash SHA-256 THẬT của các file
# hệ thống quan trọng, xuất ra đúng định dạng known_hashes.json.
#
# Cách dùng: chmod +x generate_known_hashes.sh && ./generate_known_hashes.sh

OUTPUT="known_hashes.json"

# Danh sách các file quan trọng cần baseline - thêm/bớt tùy nhu cầu.
FILES=(
  "/usr/sbin/sshd:Dịch vụ SSH hệ thống"
  "/usr/bin/bash:Shell mặc định"
  "/usr/bin/sudo:Công cụ nâng quyền"
  "/usr/bin/su:Chuyển đổi user"
  "/usr/bin/passwd:Đổi mật khẩu user"
  "/usr/sbin/cron:Lịch trình chạy tác vụ định kỳ"
  "/usr/bin/ssh:Client SSH"
  "/usr/bin/curl:Tải dữ liệu qua HTTP/HTTPS"
  "/usr/bin/wget:Tải dữ liệu qua HTTP/HTTPS"
  "/usr/sbin/nginx:Web server Nginx (nếu có cài)"
  "/usr/sbin/apache2:Web server Apache (nếu có cài)"
  "/usr/bin/systemctl:Quản lý dịch vụ systemd"
  "/usr/bin/python3:Trình thông dịch Python 3"
  "/usr/bin/mysqld:Cơ sở dữ liệu MySQL (nếu có cài)"
  "/usr/sbin/mysqld:Cơ sở dữ liệu MySQL (đường dẫn thay thế)"
)

echo "[" > "$OUTPUT"
first=true

for entry in "${FILES[@]}"; do
  path="${entry%%:*}"
  desc="${entry#*:}"

  # Bỏ qua file không tồn tại trên máy này (vd chưa cài nginx/mysql).
  if [ ! -f "$path" ]; then
    continue
  fi

  hash=$(sha256sum "$path" | awk '{print $1}')

  if [ "$first" = true ]; then
    first=false
  else
    echo "," >> "$OUTPUT"
  fi

  cat >> "$OUTPUT" << EOF
  {
    "exePath": "$path",
    "sha256": "$hash",
    "description": "$desc"
  }
EOF
done

echo "" >> "$OUTPUT"
echo "]" >> "$OUTPUT"

echo "Đã tạo $OUTPUT với $(grep -c exePath "$OUTPUT") file."