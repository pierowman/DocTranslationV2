"""
PDF Image Replacement Service using PyMuPDF (fitz)
This service receives a translated PDF and replaces images at their original positions
"""

from flask import Flask, request, send_file, jsonify
import fitz  # PyMuPDF
import json
import io
import logging

app = Flask(__name__)
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


@app.route('/health', methods=['GET'])
def health_check():
    """Health check endpoint"""
    return jsonify({"status": "healthy", "service": "PDF Image Replacement"}), 200


@app.route('/replace-images', methods=['POST'])
def replace_images():
    """
    Replace images in a PDF document while maintaining exact positions
    
    Expected form data:
    - translated_pdf: PDF file (already translated text)
    - image_mappings: JSON array with position information
    - translated_images: Multiple image files
    
    Returns:
    - PDF file with images replaced
    """
    try:
        logger.info("Received image replacement request")
        
        # Validate request
        if 'translated_pdf' not in request.files:
            return jsonify({"error": "Missing translated_pdf"}), 400
        
        if 'image_mappings' not in request.form:
            return jsonify({"error": "Missing image_mappings"}), 400
        
        # Get translated PDF
        translated_pdf = request.files['translated_pdf']
        pdf_bytes = translated_pdf.read()
        
        # Get image mappings
        mappings = json.loads(request.form['image_mappings'])
        logger.info(f"Processing {len(mappings)} image mappings")
        
        # Get translated images
        translated_images = request.files.getlist('translated_images')
        if len(translated_images) != len(mappings):
            logger.warning(
                f"Image count mismatch: {len(translated_images)} images, "
                f"{len(mappings)} mappings"
            )
        
        # Open PDF document
        doc = fitz.open(stream=pdf_bytes, filetype="pdf")
        logger.info(f"Opened PDF with {doc.page_count} pages")
        
        # Replace each image
        replaced_count = 0
        for i, mapping in enumerate(mappings):
            try:
                page_num = mapping.get('page_number', 0)
                
                # Validate page number
                if page_num < 0 or page_num >= doc.page_count:
                    logger.warning(f"Invalid page number: {page_num}")
                    continue
                
                page = doc[page_num]
                
                # Get position
                x = mapping.get('x', 0)
                y = mapping.get('y', 0)
                width = mapping.get('width', 100)
                height = mapping.get('height', 100)
                
                # Create rectangle for image placement
                rect = fitz.Rect(x, y, x + width, y + height)
                
                # Get corresponding translated image
                if i < len(translated_images):
                    image_data = translated_images[i].read()
                    
                    # Remove old image by drawing white rectangle
                    page.draw_rect(rect, color=(1, 1, 1), fill=(1, 1, 1))
                    
                    # Insert new image at exact position
                    page.insert_image(rect, stream=image_data)
                    
                    replaced_count += 1
                    logger.info(
                        f"Replaced image {i} on page {page_num} at "
                        f"({x}, {y}, {width}, {height})"
                    )
                else:
                    logger.warning(f"Missing image data for mapping {i}")
                    
            except Exception as img_ex:
                logger.error(f"Error replacing image {i}: {str(img_ex)}")
                # Continue with other images
        
        logger.info(f"Successfully replaced {replaced_count}/{len(mappings)} images")
        
        # Save result to memory
        output = io.BytesIO()
        doc.save(output)
        doc.close()
        output.seek(0)
        
        # Return modified PDF
        return send_file(
            output,
            mimetype='application/pdf',
            as_attachment=True,
            download_name='result.pdf'
        )
        
    except json.JSONDecodeError as e:
        logger.error(f"Invalid JSON in image_mappings: {str(e)}")
        return jsonify({"error": f"Invalid JSON: {str(e)}"}), 400
        
    except Exception as e:
        logger.error(f"Error processing PDF: {str(e)}", exc_info=True)
        return jsonify({"error": str(e)}), 500


if __name__ == '__main__':
    logger.info("Starting PDF Image Replacement Service")
    app.run(host='0.0.0.0', port=5000, debug=False)
