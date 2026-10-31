from fractions import Fraction
from collections import defaultdict
import json
from pathlib import Path

# Independent conservative draw-horizon process: all eight Danger copies
# produce one draw, both Allure copies two, Graceful three; no discards/locks.
# Ordinary includes the search source, since search does not initiate draws.
results = []
path=Path('research/danger-fast/review-horizon-results.json')
for n, cap in ((40, 8), (60, 8), (40, 4), (60, 4)):
    capacities=(8,2,1,n-11)
    live={(0,0,0,0): Fraction(1)}
    tails=[]
    for depth in range(n+1):
        mass=sum(live.values(), Fraction())
        tails.append({'depth_at_least':depth,'probability':str(mass),'decimal':float(mass),'states':len(live)})
        next_live=defaultdict(Fraction)
        for counts,p in live.items():
            slots=5-depth+min(counts[0],cap)+2*counts[1]+3*counts[2]
            if slots==0 or depth==n: continue
            for i,c in enumerate(counts):
                left=capacities[i]-c
                if left:
                    child=list(counts);child[i]+=1
                    next_live[tuple(child)]+=p*Fraction(left,n-depth)
        live=next_live
        if not live: break
    results.append({'deck_size':n,'danger_credit_cap':cap,'tails':tails})
    temporary=path.with_suffix('.json.tmp')
    temporary.write_text(json.dumps(results,indent=2)+'\n')
    temporary.replace(path)
for result in results:
 print(result['deck_size'],result['danger_credit_cap'], [(r['depth_at_least'],round(100*r['decimal'],5),r['states']) for r in result['tails'] if r['depth_at_least']>=8])

