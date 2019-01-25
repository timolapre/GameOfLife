__kernel void Simulate
(
   __global uint* pattern,
   __global uint* second,
   uint pw,
   uint ph
 )
{
	int i = get_global_id(0);
	uint y = 1+i/(pw*32-2);
	uint x = 1+i%(pw*32-2);

	uint up = ((second[(y+1) * pw + ((x) >> 5)] >> (int)((x) & 31)) & 1U);
	uint down = ((second[(y-1) * pw + ((x) >> 5)] >> (int)((x) & 31)) & 1U);
	uint right = ((second[(y) * pw + ((x+1) >> 5)] >> (int)((x+1) & 31)) & 1U);
	uint left = ((second[(y) * pw + ((x-1) >> 5)] >> (int)((x-1) & 31)) & 1U);
	uint upleft = ((second[(y+1) * pw + ((x-1) >> 5)] >> (int)((x-1) & 31)) & 1U);
	uint downleft = ((second[(y-1) * pw + ((x-1) >> 5)] >> (int)((x-1) & 31)) & 1U);
	uint upright = ((second[(y+1) * pw + ((x+1) >> 5)] >> (int)((x+1) & 31)) & 1U);
	uint downright = ((second[(y-1) * pw + ((x+1) >> 5)] >> (int)((x+1) & 31)) & 1U);

	uint n = up + down + right + left + upleft + downleft + upright + downright;

	if (( ((second[y * pw + (x >> 5)] >> (int)(x & 31)) & 1U) == 1 && n ==2) || n == 3)
	{
		//pattern[y * pw + (x >> 5)] = pattern[y * pw + (x >> 5)] | 1U << (int)(x & 31);
		atom_add(&pattern[y * pw + (x >> 5)], pattern[y * pw + (x >> 5)] | 1U << (int)(x & 31));
	}
		
	// GETBIT : ((second[y * pw + (x >> 5)] >> (int)(x & 31)) & 1U);
	// SETBIT : pattern[y * pw + (x >> 5)] |= 1U << (int)(x & 31);
}