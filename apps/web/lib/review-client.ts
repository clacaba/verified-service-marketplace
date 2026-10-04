export type ProviderReply = {comment:string;createdAt:string;authorLabel:string};
export type CustomerReview = {rating:number;comment:string;createdAt:string;status?:"Published"|"Hidden";authorLabel?:string;reply?:ProviderReply|null};
export type BookingReviewState = {canReview:boolean;reason:string;canReply?:boolean;replyReason?:string;review:CustomerReview|null};
export type PublicReviews = {items:CustomerReview[];totalCount:number;averageRating:number|null;page:number;pageSize:number;disclosure:string};
export function validateReview(rating:number,comment:string,publishAcknowledged:boolean){
 const errors:{rating?:string;comment?:string;publishAcknowledged?:string}={};
 if(!Number.isInteger(rating)||rating<1||rating>5)errors.rating="Choose a rating from 1 to 5 stars.";
 if(comment.trim().length<10||comment.trim().length>2000||/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/.test(comment))errors.comment="Write 10–2000 characters. Line breaks and tabs are allowed.";
 if(!publishAcknowledged)errors.publishAcknowledged="Confirm that your rating and feedback will be public.";
 return errors;
}
export function ratingText(rating:number){return `${rating} out of 5 stars`;}
export function validateReply(comment:string,publishAcknowledged:boolean){const {rating,...errors}=validateReview(1,comment,publishAcknowledged);return errors;}
