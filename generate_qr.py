import qrcode
import os

def generate_qr(url, output_path):
    # 创建目录
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    
    # 配置二维码参数
    qr = qrcode.QRCode(
        version=1,
        error_correction=qrcode.constants.ERROR_CORRECT_H, # 高纠错等级
        box_size=10,
        border=4,
    )
    qr.add_data(url)
    qr.make(fit=True)

    # 生成图片
    img = qr.make_image(fill_color="black", back_color="white")
    img.save(output_path)
    print(f"QR Code generated successfully at: {output_path}")

if __name__ == "__main__":
    # 使用 GitHub 仓库作为示例下载链接
    download_url = "https://github.com/xuhao/ModbusMonitor"
    output_file = r"e:\phm2\PHM_WEB\images\download_qr.png"
    
    generate_qr(download_url, output_file)
