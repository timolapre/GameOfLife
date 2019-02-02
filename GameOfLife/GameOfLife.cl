uint GetBit( uint x, uint y, __global uint* second, uint pw ){return (second[y * pw + (x >> 5)] >> (int)(x & 31)) & 1U;}

uint mod(int x, int y)
{
    int res = x % y;
    if ((res > 0 && y < 0) || (res < 0 && y > 0))  
	{
        res += y;
	}
	return res;
}

__kernel void Simulate
(
   __global uint* pattern,
   __global uint* second,
   uint pw,
   uint ph
 )
{
	int i = get_global_id(0);
	uint y = i/(pw*32);
	uint x = i%(pw*32);

	pattern[y * pw + (x >> 5)] = 0;

	uint up = 0;
	uint down = 0;
	uint right = 0;
	uint left = 0;
	uint upleft = 0;
	uint downleft = 0;
	uint upright = 0;
	uint downright = 0;

	if(y != ph){up = GetBit(x,y+1, second, pw);}
	if(y != 0){down = GetBit(x,y-1, second, pw);}
	if(x != pw){right = GetBit(x+1,y, second, pw);}
	if(x != 0){left = GetBit(x-1,y, second, pw);}
	if(y != ph && x != 0){upleft = GetBit(x-1,y+1, second, pw);}
	if(y != 0 && x != 0){downleft = GetBit(x-1,y-1, second, pw);}
	if(y != ph && x != pw){upright = GetBit(x+1,y+1, second, pw);}
	if(y != 0 && x != pw){downright = GetBit(x+1,y-1, second, pw);}

	uint n = up + down + right + left + upleft + downleft + upright + downright;

	if (( GetBit(x,y,second,pw) == 1 && n ==2) || n == 3)
	{
		atom_add(&pattern[y * pw + (x >> 5)], 1U << (int)(x & 31));
	}
}

__kernel void SimulateWrap
(
   __global uint* pattern,
   __global uint* second,
   uint pw,
   uint ph
 )
{
	int i = get_global_id(0);
	uint y = i/(pw*32);
	uint x = i%(pw*32);

	pattern[y * pw + (x >> 5)] = 0;

	uint up = GetBit(x,mod(y+1,ph), second, pw);
	uint down = GetBit(x,mod(y-1,ph), second, pw);
	uint right = GetBit(mod(x+1,pw*32),y, second, pw);
	uint left = GetBit(mod(x-1,pw*32),y, second, pw);
	uint upleft = GetBit(mod(x-1,pw*32),mod(y+1,ph), second, pw);
	uint downleft = GetBit(mod(x-1,pw*32),mod(y-1,ph), second, pw);
	uint upright = GetBit(mod(x+1,pw*32),mod(y+1,ph), second, pw);
	uint downright = GetBit(mod(x+1,pw*32),mod(y-1,ph), second, pw);

	uint n = up + down + right + left + upleft + downleft + upright + downright;

	if (( GetBit(x,y,second,pw) == 1 && n ==2) || n == 3)
	{
		atom_add(&pattern[y * pw + (x >> 5)], 1U << (int)(x & 31));
	}
}