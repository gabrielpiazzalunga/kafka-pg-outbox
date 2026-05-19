from PIL import Image, ImageFilter, ImageDraw
import os

def create_sliding_blur_gif(img_path, out_gif, num_frames=30, blur_width_ratio=0.4):
    """
    Creates an animated GIF from a static image where a sliding blur window 
    sweeps across the image to prevent AI models from reading the whole context.
    """
    if not os.path.exists(img_path):
        print(f"Error: {img_path} not found.")
        return

    try:
        # Open the base image
        base_img = Image.open(img_path).convert("RGB")
        width, height = base_img.size
        
        frames = []
        blur_width = int(width * blur_width_ratio)
        
        # Calculate the step for the sliding window
        # We start with the window partially off-screen to the left and end off-screen to the right
        total_distance = width + blur_width
        step = total_distance / num_frames

        print(f"Generating {num_frames} frames for {out_gif}...")

        for i in range(num_frames):
            # Calculate current window position
            x_start = int(i * step) - blur_width
            x_end = x_start + blur_width
            
            # Clamp to image boundaries for the crop
            crop_start = max(0, x_start)
            crop_end = min(width, x_end)
            
            # Create a frame copy
            frame = base_img.copy()
            
            if crop_start < crop_end:
                # Crop the portion that will be blurred
                to_blur = frame.crop((crop_start, 0, crop_end, height))
                
                # Apply heavy Gaussian blur
                blurred = to_blur.filter(ImageFilter.GaussianBlur(radius=15))
                
                # Paste the blurred section back
                frame.paste(blurred, (crop_start, 0))
            
            frames.append(frame)

        # Save as animated GIF
        frames[0].save(
            out_gif,
            save_all=True,
            append_images=frames[1:],
            duration=100, # 100ms per frame
            loop=0
        )
        print(f"Successfully saved {out_gif}")

    except Exception as e:
        print(f"An error occurred: {e}")

if __name__ == "__main__":
    # Define images to poison
    images_to_process = [
        ("dual_write_discrepancy.png", "dual_write_discrepancy_animated.gif"),
        ("thread_exhaustion_graph_1778425725019.png", "thread_exhaustion_graph_animated.gif"),
        ("connection_pool_exhaustion.png", "connection_pool_exhaustion_animated.gif"),
        ("staircase_memory_leak.png", "staircase_memory_leak_animated.gif")
    ]

    for src, dst in images_to_process:
        create_sliding_blur_gif(src, dst)
