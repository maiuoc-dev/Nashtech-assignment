import { createSlice, createAsyncThunk, PayloadAction } from '@reduxjs/toolkit';
import { Order } from '../../../types/globalTypes';
import axiosConfig from '../../config/axios.config';

interface OrdersState {
  totalOrders: number;
  orders: Order[];
  status: 'idle' | 'loading' | 'succeeded' | 'failed';
  error: string | null;
}

const initialState: OrdersState = {
  totalOrders: 0,
  orders: [],
  status: 'idle',
  error: null
};

// Async thunks
export const fetchOrders = createAsyncThunk(
  'orders/fetchOrders',
  async () => {
    const response = await axiosConfig.get('http://localhost:5113/api/Order');
    return response.data;
  }
);

export const getDetailsOrder = createAsyncThunk(
  'orders/getDetailsOrder',
  async (id: number) => {
    const response = await axiosConfig.get(`http://localhost:5113/api/Order/${id}`);
    return response.data;
  }
)

const ordersSlice = createSlice({
  name: 'orders',
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      // Fetch orders cases
      .addCase(fetchOrders.pending, (state) => {
        state.status = 'loading';
      })
      .addCase(fetchOrders.fulfilled, (state, action: PayloadAction<{ totalOrders: number; orders: Order[] }>) => {
        state.status = 'succeeded';
        state.totalOrders = action.payload.totalOrders;
        state.orders = action.payload.orders;
        state.error = null;
      })
      .addCase(fetchOrders.rejected, (state, action) => {
        state.status = 'failed';
        state.error = action.error.message || 'Failed to fetch orders';
      })
  },
});

export default ordersSlice.reducer;