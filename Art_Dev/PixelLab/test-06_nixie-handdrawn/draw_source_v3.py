from PIL import Image
import os, math
SP = r"C:/Users/kailg/AppData/Local/Temp/claude/C--My-Projects-Guildmaster-Autobattler/3f533fc5-1b26-42f1-9119-73eec9e483ad/scratchpad"
W = H = 64

# material ramps [dark, mid, light, hilite]
RAMP = {
 'steel':  [(45,49,70),(86,94,114),(140,150,170),(198,205,218)],
 'cloth':  [(28,38,66),(52,78,120),(88,122,168),(120,150,196)],
 'wood':   [(58,40,26),(120,84,50),(160,118,74),(190,150,100)],
 'leather':[(48,34,24),(96,66,42),(150,108,66),(180,140,90)],
 'skin':   [(110,74,44),(168,120,78),(214,168,116),(236,200,150)],
 'gold':   [(150,92,34),(201,168,89),(240,210,110),(255,240,170)],
 'teal':   [(40,90,86),(70,150,150),(118,191,204),(180,225,230)],
}
OL = (10,10,18)
col = [[None]*W for _ in range(H)]   # color grid
mat = [[None]*W for _ in range(H)]   # material grid

def setp(x,y,m,shade=1):
    if 0<=x<W and 0<=y<H:
        col[y][x]=RAMP[m][shade]; mat[y][x]=m

def disk(cx,cy,r,m,shade=1):
    for y in range(int(cy-r),int(cy+r)+1):
        for x in range(int(cx-r),int(cx+r)+1):
            if (x-cx)**2+(y-cy)**2<=r*r: setp(x,y,m,shade)

def thick(p0,p1,rad,m,shade=1):
    x0,y0=p0; x1,y1=p1; n=int(max(abs(x1-x0),abs(y1-y0)))+1
    for i in range(n+1):
        t=i/n; disk(x0+(x1-x0)*t, y0+(y1-y0)*t, rad, m, shade)

def poly(pts,m,shade=1):
    ys=[p[1] for p in pts]
    for y in range(int(min(ys)),int(max(ys))+1):
        xs=[]
        for i in range(len(pts)):
            x0,y0=pts[i]; x1,y1=pts[(i+1)%len(pts)]
            if (y0<=y<y1) or (y1<=y<y0):
                xs.append(x0+(x1-x0)*(y-y0)/(y1-y0))
        xs.sort()
        for j in range(0,len(xs)-1,2):
            for x in range(int(round(xs[j])),int(round(xs[j+1]))+1): setp(x,y,m,shade)

# =================== COMPOSITION: braced defender, facing right ===================
# back (far) leg — planted, grounded, thicker, more vertical
thick((29,45),(28,51),2.7,'steel'); thick((28,51),(28,56),2.6,'steel')
disk(28,57,2.6,'leather')  # back boot
# front (near) leg — slight forward stagger, thick
thick((34,45),(35,51),2.9,'steel'); thick((35,51),(35,56),2.7,'steel')
disk(36,57,2.7,'leather')  # front boot

# torso (leaning forward-right), mail  — wider, solid
poly([(27,29),(38,31),(37,46),(26,45)],'steel')
disk(27,30,3.6,'steel'); disk(37,31,3.8,'steel')  # shoulders/pauldrons rounded
# tabard over torso (blue), angled hem
poly([(28,31),(36,33),(37,48),(33,51),(30,49),(27,47)],'cloth')
# belt
poly([(27,44),(37,45),(37,47),(27,46)],'leather')
setp(31,45,'gold'); setp(32,45,'gold')

# rear arm (sword arm): thick shoulder -> elbow -> raised hand
thick((28,31),(24,28),2.7,'steel')     # upper arm (thicker)
thick((24,28),(21,25),2.3,'steel')     # forearm up
disk(20,24,2.0,'leather')              # gauntlet/grip
# SWORD raised up-left (diagonal silhouette hook), slightly calmer angle
thick((20,24),(15,11),1.2,'steel',2)   # blade
setp(20,25,'gold'); setp(19,25,'gold'); setp(21,25,'gold'); setp(20,26,'gold')  # crossguard
setp(15,11,'steel',3); setp(16,12,'steel',3)                 # tip glint

# head / open helm (bigger, clearer, heroic)
disk(32,21,5.0,'steel')                 # helm dome (bigger, raised)
poly([(30,20),(35,20),(35,17),(31,17)],'steel')  # helm brow ridge
# face — larger, lit
poly([(29,23),(35,24),(35,29),(30,29),(29,27)],'skin')
# neck
poly([(30,29),(34,30),(33,32),(30,31)],'skin')

# front arm (shield arm) — bicep hint before shield covers it
thick((36,32),(41,35),2.5,'steel')

# BIG SHIELD (front-most mass, lower-right, slight tilt) — the silhouette hook
SCX,SCY,SR=43,37,10
for y in range(SCY-SR,SCY+SR+1):
    for x in range(SCX-SR,SCX+SR+1):
        dx,dy=x-SCX,y-SCY; dd=dx*dx+dy*dy
        if dd<=SR*SR:
            if dd>=(SR-1.2)**2: setp(x,y,'wood',0)      # rim dark
            elif dd>=(SR-3)**2: setp(x,y,'leather',1)   # leather band
            else: setp(x,y,'wood',1)                    # face
# gold boss
disk(SCX,SCY,2.4,'gold',1); setp(SCX-1,SCY-1,'gold',3)
# a couple rim studs
for a in (0.4,1.9,3.6,5.0):
    setp(int(SCX+(SR-2)*math.cos(a)),int(SCY+(SR-2)*math.sin(a)),'gold',2)

# =================== FORM PASS: light from top-left via edges ===================
base_col=[row[:] for row in col]; base_mat=[row[:] for row in mat]
def opaque(x,y): return 0<=x<W and 0<=y<H and base_col[y][x] is not None
out=[[None]*W for _ in range(H)]
for y in range(H):
    for x in range(W):
        if base_col[y][x] is None: continue
        m=base_mat[y][x]; r=RAMP[m]
        up=not opaque(x,y-1); left=not opaque(x-1,y); ul=not opaque(x-1,y-1)
        dn=not opaque(x,y+1); rt=not opaque(x+1,y); dr=not opaque(x+1,y+1)
        light=up or left or ul
        shadow=dn or rt or dr
        cur=base_col[y][x]
        # keep already-hilited/shaded specials (blade tip, boss) roughly
        if light and not shadow: out[y][x]=r[2] if cur==r[1] else cur
        elif shadow and not light: out[y][x]=r[0] if cur==r[1] else cur
        else: out[y][x]=cur
        # extra top-left rim highlight
        if (up and left): out[y][x]=r[3] if cur in (r[1],r[2]) else out[y][x]
col=out

# =================== DETAILS: face, sword sheen ===================
def put(x,y,c):
    if 0<=x<W and 0<=y<H: col[y][x]=c
# face detail
put(31,25,OL); put(34,25,OL)               # eyes
put(30,24,RAMP['skin'][3]); put(31,24,RAMP['skin'][2])  # cheek/forehead light
put(31,27,RAMP['skin'][0]); put(32,27,RAMP['skin'][0]); put(33,27,RAMP['skin'][0])  # beard shadow
put(32,25,RAMP['steel'][1]); put(32,26,RAMP['steel'][0])  # nasal guard
# teal plume on helm crest
put(31,15,RAMP['teal'][2]); put(32,15,RAMP['teal'][2]); put(32,14,RAMP['teal'][3]); put(31,16,RAMP['teal'][1])
# tabard emblem (small teal chevron)
put(31,40,RAMP['teal'][2]); put(32,41,RAMP['teal'][2]); put(30,41,RAMP['teal'][1]); put(31,42,RAMP['teal'][1])

# =================== OUTLINE ===================
img=Image.new('RGBA',(W,H),(0,0,0,0)); px=img.load()
for y in range(H):
    for x in range(W):
        if col[y][x] is not None: px[x,y]=col[y][x]+(255,)
def op(x,y): return 0<=x<W and 0<=y<H and col[y][x] is not None
for y in range(H):
    for x in range(W):
        if col[y][x] is None and any(op(x+dx,y+dy) for dx,dy in((1,0),(-1,0),(0,1),(0,-1))):
            px[x,y]=OL+(255,)

img.save(os.path.join(SP,'knight2.png'))
img.resize((W*8,H*8),Image.NEAREST).save(os.path.join(SP,'knight2_x8.png'))
n=len([c for c in img.getcolors(9999) if c[1][3]>0]); print('knight2 drawn, colors',n)
