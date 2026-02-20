# Quick Guide: Image Processing Option

## What You'll See in the UI

### Upload Section
```
???????????????????????????????????????????????????????????
?  Select Documents                                        ?
?  [Choose Files] 3 files selected                        ?
?                                                          ?
?  • marketing_brochure.docx [500 KB] ???                  ?
?  • financial_report.pdf [1.2 MB] ???                     ?
?  • data_table.xlsx [250 KB]                             ?
???????????????????????????????????????????????????????????
```
**Note:** ??? icon means image processing is available for this file

### Image Processing Option
```
???????????????????????????????????????????????????????????
?  Image Processing                                        ?
?  ?? Process Images in Documents                         ?
?     Extract and translate images from Word and PDF      ?
?     documents                                            ?
?                                                          ?
?  ? When enabled: Images extracted, translated, replaced ?
?  ?? When disabled: Text translated, images unchanged    ?
?  Applies to: .docx and .pdf files                       ?
???????????????????????????????????????????????????????????
```

---

## Decision Tree: Should I Enable Image Processing?

```
Does your document have images?
    ?
    ?? NO ? ?? DISABLE (faster, no benefit to enabling)
    ?
    ?? YES ? Do the images contain text that needs translation?
              ?
              ?? NO (decorative images) ? ?? DISABLE (faster)
              ?
              ?? YES ? Do you need professional/complete translation?
                       ?
                       ?? NO (draft/preview) ? ?? DISABLE (faster)
                       ?
                       ?? YES ? ? ENABLE (full translation)
```

---

## Common Scenarios

### ? ENABLE Image Processing

**Scenario 1: Marketing Materials**
```
Document: product_brochure.docx
Images: Product photos with English labels
Need: Translate to Spanish for international customers
Action: ? Enable image processing
Result: Labels translated to Spanish
```

**Scenario 2: Training Manual**
```
Document: user_guide.pdf
Images: Screenshots with UI text in English
Need: Localize for French users
Action: ? Enable image processing
Result: UI text in screenshots translated
```

**Scenario 3: Presentation**
```
Document: sales_pitch.pptx
Images: Charts with English titles and legends
Need: Present to Japanese clients
Action: ? Enable image processing
Result: Charts fully translated
```

### ?? DISABLE Image Processing

**Scenario 1: Technical Paper**
```
Document: research_paper.docx
Images: Generic charts/graphs with universal symbols
Need: Quick translation for review
Action: ?? Disable image processing
Result: Faster, images don't need translation
```

**Scenario 2: Simple Report**
```
Document: monthly_report.pdf
Images: Company logo, decorative photos
Need: Translate text content only
Action: ?? Disable image processing
Result: Much faster processing
```

**Scenario 3: Draft Translation**
```
Document: contract_draft.docx
Images: Signatures, stamps (already finalized)
Need: Quick draft to understand content
Action: ?? Disable image processing
Result: Fast turnaround for draft
```

---

## Performance Comparison

### Small Document (document.docx - 500 KB, 5 images)

**With Image Processing Enabled:**
```
?? Processing Time: ~5 seconds
?? Steps:
  1. Upload document
  2. Extract 5 images
  3. Create images PDF
  4. Translate text + images
  5. Replace images in result
?? Files Created: 3 (document, images PDF, metadata)
```

**With Image Processing Disabled:**
```
?? Processing Time: ~2 seconds
?? Steps:
  1. Upload document
  2. Translate text only
?? Files Created: 1 (document)
```

**Time Saved: 60% faster ?**

### Large PDF (report.pdf - 5 MB, 20 images)

**With Image Processing Enabled:**
```
?? Processing Time: ~25 seconds
?? Steps:
  1. Upload PDF
  2. Extract 20 images
  3. Create images PDF
  4. Translate text + images
  5. Python service replaces images
?? Files Created: 3 + Python processing
```

**With Image Processing Disabled:**
```
?? Processing Time: ~8 seconds
?? Steps:
  1. Upload PDF
  2. Translate text only
?? Files Created: 1
```

**Time Saved: 68% faster ??**

---

## Visual Workflow

### Enabled Workflow
```
?? Document Upload
    ?
?? Image Detection ? Found 5 images
    ?
??? Image Extraction
    ?
?? Create Images PDF
    ?
?? Store Metadata
    ?
?? Translate Text + Images
    ?
?? Replace Images
    ?
? Complete: Fully translated document
```

### Disabled Workflow
```
?? Document Upload
    ?
?? Image Processing: SKIPPED
    ?
?? Translate Text Only
    ?
? Complete: Text translated, images unchanged
```

---

## Tips & Tricks

### ?? Tip 1: Check File Indicators
Look for the ??? icon next to filenames - it shows which files support image processing.

### ?? Tip 2: Use for Drafts
Disable image processing for quick draft translations, then enable for final version.

### ?? Tip 3: Batch Processing
If translating many documents:
- Technical docs ? Disable
- Marketing materials ? Enable
- Process in separate batches

### ?? Tip 4: File Type Awareness
```
? Supports Images: .docx, .pdf
? No Image Support: .txt, .xlsx, .html, .rtf
```
For non-supported types, the checkbox setting doesn't matter.

### ?? Tip 5: Test First
When unsure, enable for one document and review. If images didn't need translation, disable for remaining batch.

---

## Keyboard Shortcuts

- **Space** while checkbox focused: Toggle on/off
- **Tab** to navigate to checkbox
- **Enter** while on submit button: Start translation

---

## Mobile/Tablet View

The checkbox adapts for smaller screens:

```
????????????????????????????????
? Image Processing             ?
? ?? Process Images            ?
?                              ?
? ? Enabled: Images replaced  ?
? ?? Disabled: Images original ?
????????????????????????????????
```

---

## Accessibility

- ? Screen reader support
- ? Keyboard navigation
- ? Clear labels and help text
- ? Visual indicators (icons + text)
- ? Color-blind friendly (uses icons not just colors)

---

## FAQ

**Q: What happens if I forget to check/uncheck?**  
A: Default is ENABLED. If you want faster processing without images, remember to uncheck.

**Q: Can I change my mind after starting?**  
A: No, the option is set when you start translation. You'd need to start a new translation.

**Q: Do Excel files support image processing?**  
A: No, currently only .docx and .pdf files support image processing.

**Q: What if my PDF has 100 images?**  
A: Consider disabling if only some images need translation. All images are processed if enabled.

**Q: Does this affect text translation quality?**  
A: No, text translation quality is the same either way. Only affects image handling.

---

## Summary

| Setting | Best For | Processing Time | Output |
|---------|----------|-----------------|--------|
| ? **Enabled** | Marketing, presentations, professional docs | Slower | Fully translated (text + images) |
| ?? **Disabled** | Technical docs, drafts, text-only docs | Faster | Text translated, images original |

**Default:** ? ENABLED (recommended for best out-of-box experience)

**When to disable:** Speed is priority and images don't contain text or don't need translation.
