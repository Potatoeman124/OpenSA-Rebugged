import json, pathlib
from PIL import Image, ImageDraw, ImageFont
root=pathlib.Path(__file__).resolve().parents[2]
batch=root/"artifacts/rmg/reassessment-comparison/regions-final-01"
manifest=json.loads((batch/"manifest.json").read_text(encoding="utf-8"))
review=batch/"review";review.mkdir(exist_ok=True)
font=ImageFont.truetype("C:/Windows/Fonts/arial.ttf",18)
small=ImageFont.truetype("C:/Windows/Fonts/arial.ttf",14)
for group in range(5):
    cases=manifest["cases"][group*3:group*3+3]
    sheet=Image.new("RGB",(1800,665),(20,23,27));draw=ImageDraw.Draw(sheet)
    crops=Image.new("RGB",(1536,1284),(20,23,27)); cd=ImageDraw.Draw(crops)
    for col,case in enumerate(cases):
        folder=batch/case["id"]; ref=json.loads((folder/"actual/reference.json").read_text(encoding="utf-8"))
        im=Image.open(folder/"actual/textures.png").convert("RGB").resize((588,588),Image.Resampling.LANCZOS)
        d=ImageDraw.Draw(im);side=ref["size"]
        for pos in ref["neutral_preview_positions"]:
            x=(pos["x"]-2+.5)*588/side;y=(pos["y"]-2+.5)*588/side
            d.rectangle((x-3,y-3,x+3,y+3),fill=(0,0,0));d.rectangle((x-2,y-2,x+2,y+2),fill=(160,160,160))
        for i,pos in enumerate(ref["player_starts"]):
            x=(pos["x"]-2+.5)*588/side;y=(pos["y"]-2+.5)*588/side
            d.ellipse((x-8,y-8,x+8,y+8),fill=(20,20,20),outline="white")
            d.text((x-5,y-8),chr(65+i),font=small,fill="white")
        sheet.paste(im,(600*col+6,64))
        draw.text((600*col+8,2),case["id"],font=font,fill="white")
        draw.text((600*col+8,23),"Seed "+case["settings"]["seed"],font=small,fill="white")
        draw.text((600*col+8,42),"Actual terrain; package positions overlaid (not a UI screenshot)",font=small,fill=(180,186,194))
        for c,label in enumerate(["north-west","center","south-east","largest-dirt"]):
            crop=Image.open(folder/f"actual/crop-{label}.png").convert("RGB").resize((384,384),Image.Resampling.LANCZOS)
            crops.paste(crop,(384*c,428*col+42))
            cd.text((384*c+6,428*col+4),case["id"]+" / "+label,font=small,fill="white")
            cd.text((384*c+6,428*col+22),"32 x 32 native cells - actual saved-map textures",font=small,fill=(180,186,194))
    sheet.save(review/f"whole-{group+1}.jpg",quality=92)
    crops.save(review/f"crops-{group+1}.jpg",quality=93)
print(review)
